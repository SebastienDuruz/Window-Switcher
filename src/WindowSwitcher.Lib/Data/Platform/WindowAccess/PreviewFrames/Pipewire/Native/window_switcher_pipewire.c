#define _GNU_SOURCE

/*
 * Native PipeWire capture adapter for Window Switcher.
 *
 * The connection, negotiation and buffer-consumption sequence is derived from
 * OBS Studio's plugins/linux-pipewire/pipewire.c (GPL-2.0-or-later), adapted
 * to expose CPU-mapped RGB frames through a small C ABI.
 * OBS PipeWire implementation copyright 2020 Georges Basile Stavracas Neto.
 *
 * SPDX-License-Identifier: GPL-3.0-or-later
 */

#include <errno.h>
#include <fcntl.h>
#include <pthread.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#include <pipewire/pipewire.h>
#include <spa/buffer/meta.h>
#include <spa/param/video/format-utils.h>

#define WS_MAX_FRAME_BYTES (128u * 1024u * 1024u)
#define WS_WAIT_TIMEOUT_NS (10ll * 1000ll * 1000ll * 1000ll)

#if defined(__GNUC__)
#define WS_EXPORT __attribute__((visibility("default")))
#else
#define WS_EXPORT
#endif

enum ws_pixel_format {
    WS_PIXEL_FORMAT_BGRA = 0,
    WS_PIXEL_FORMAT_BGRX = 1,
    WS_PIXEL_FORMAT_RGBA = 2,
    WS_PIXEL_FORMAT_RGBX = 3,
};

typedef void (*ws_frame_callback)(void *user_data,
                                  const uint8_t *data,
                                  uint32_t accessible_size,
                                  int32_t stride,
                                  uint32_t width,
                                  uint32_t height,
                                  uint32_t pixel_format);
typedef void (*ws_state_callback)(void *user_data, int32_t state, const char *message);

struct ws_pipewire_stream {
    struct pw_thread_loop *thread_loop;
    struct pw_context *context;
    struct pw_core *core;
    struct spa_hook core_listener;
    struct pw_stream *stream;
    struct spa_hook stream_listener;
    struct spa_video_info_raw format;
    ws_frame_callback frame_callback;
    ws_state_callback state_callback;
    void *user_data;
    uint32_t target_width;
    uint32_t target_height;
    uint32_t maximum_framerate;
    int sync_sequence;
    enum pw_stream_state state;
    bool core_synchronized;
    bool faulted;
    bool unsupported_buffer_reported;
    bool loop_started;
};

static pthread_mutex_t runtime_mutex = PTHREAD_MUTEX_INITIALIZER;
static unsigned int runtime_references;

static void runtime_acquire(void)
{
    pthread_mutex_lock(&runtime_mutex);
    if (runtime_references++ == 0)
        pw_init(NULL, NULL);
    pthread_mutex_unlock(&runtime_mutex);
}

static void runtime_release(void)
{
    pthread_mutex_lock(&runtime_mutex);
    if (runtime_references > 0 && --runtime_references == 0)
        pw_deinit();
    pthread_mutex_unlock(&runtime_mutex);
}

static void report_state(struct ws_pipewire_stream *capture, int32_t state, const char *message)
{
    if (capture->state_callback)
        capture->state_callback(capture->user_data, state, message);
}

static void on_core_done(void *user_data, uint32_t id, int sequence)
{
    struct ws_pipewire_stream *capture = user_data;
    if (id == PW_ID_CORE && sequence == capture->sync_sequence) {
        capture->core_synchronized = true;
        pw_thread_loop_signal(capture->thread_loop, false);
    }
}

static void on_core_error(void *user_data,
                          uint32_t id,
                          int sequence,
                          int result,
                          const char *message)
{
    struct ws_pipewire_stream *capture = user_data;
    (void)id;
    (void)sequence;
    (void)result;
    capture->faulted = true;
    report_state(capture, PW_STREAM_STATE_ERROR, message ? message : "PipeWire core error");
    pw_thread_loop_signal(capture->thread_loop, false);
}

static const struct pw_core_events core_events = {
    PW_VERSION_CORE_EVENTS,
    .done = on_core_done,
    .error = on_core_error,
};

static struct spa_pod *build_format(struct spa_pod_builder *builder,
                                    uint32_t format,
                                    uint32_t width,
                                    uint32_t height,
                                    uint32_t maximum_framerate)
{
    struct spa_pod_frame frame;
    struct spa_rectangle resolution = SPA_RECTANGLE(width, height);
    struct spa_rectangle minimum_resolution = SPA_RECTANGLE(1, 1);
    struct spa_rectangle maximum_resolution = SPA_RECTANGLE(8192, 4320);
    struct spa_fraction framerate = SPA_FRACTION(maximum_framerate, 1);
    struct spa_fraction minimum_framerate = SPA_FRACTION(0, 1);

    spa_pod_builder_push_object(builder, &frame, SPA_TYPE_OBJECT_Format, SPA_PARAM_EnumFormat);
    spa_pod_builder_add(builder,
                        SPA_FORMAT_mediaType, SPA_POD_Id(SPA_MEDIA_TYPE_video),
                        SPA_FORMAT_mediaSubtype, SPA_POD_Id(SPA_MEDIA_SUBTYPE_raw),
                        SPA_FORMAT_VIDEO_format, SPA_POD_Id(format),
                        SPA_FORMAT_VIDEO_size,
                        SPA_POD_CHOICE_RANGE_Rectangle(
                            &resolution, &minimum_resolution, &maximum_resolution),
                        SPA_FORMAT_VIDEO_framerate,
                        SPA_POD_CHOICE_RANGE_Fraction(
                            &framerate, &minimum_framerate, &framerate),
                        0);
    return spa_pod_builder_pop(builder, &frame);
}

static uint32_t build_format_parameters(struct ws_pipewire_stream *capture,
                                        struct spa_pod_builder *builder,
                                        const struct spa_pod **parameters,
                                        uint32_t capacity)
{
    static const uint32_t formats[] = {
        SPA_VIDEO_FORMAT_BGRA,
        SPA_VIDEO_FORMAT_RGBA,
        SPA_VIDEO_FORMAT_BGRx,
        SPA_VIDEO_FORMAT_RGBx,
    };
    uint32_t count = 0;
    for (size_t index = 0; index < SPA_N_ELEMENTS(formats) && count < capacity; index++) {
        parameters[count++] = build_format(builder,
                                            formats[index],
                                            capture->target_width,
                                            capture->target_height,
                                            capture->maximum_framerate);
    }
    return count;
}

static bool map_pixel_format(uint32_t spa_format, uint32_t *pixel_format)
{
    switch (spa_format) {
    case SPA_VIDEO_FORMAT_BGRA:
        *pixel_format = WS_PIXEL_FORMAT_BGRA;
        return true;
    case SPA_VIDEO_FORMAT_BGRx:
        *pixel_format = WS_PIXEL_FORMAT_BGRX;
        return true;
    case SPA_VIDEO_FORMAT_RGBA:
        *pixel_format = WS_PIXEL_FORMAT_RGBA;
        return true;
    case SPA_VIDEO_FORMAT_RGBx:
        *pixel_format = WS_PIXEL_FORMAT_RGBX;
        return true;
    default:
        return false;
    }
}

static void on_parameter_changed(void *user_data, uint32_t id, const struct spa_pod *parameter)
{
    struct ws_pipewire_stream *capture = user_data;
    struct spa_pod_builder builder;
    const struct spa_pod *parameters[5];
    uint8_t parameter_buffer[1024];
    uint32_t media_type;
    uint32_t media_subtype;
    uint32_t pixel_format;
    uint32_t count = 0;

    if (!parameter || id != SPA_PARAM_Format)
        return;
    if (spa_format_parse(parameter, &media_type, &media_subtype) < 0 ||
        media_type != SPA_MEDIA_TYPE_video || media_subtype != SPA_MEDIA_SUBTYPE_raw ||
        spa_format_video_raw_parse(parameter, &capture->format) < 0 ||
        !map_pixel_format(capture->format.format, &pixel_format) ||
        capture->format.size.width == 0 || capture->format.size.height == 0 ||
        (uint64_t)capture->format.size.width * capture->format.size.height * 4u > WS_MAX_FRAME_BYTES) {
        capture->faulted = true;
        report_state(capture, PW_STREAM_STATE_ERROR, "Unsupported negotiated video format");
        pw_thread_loop_signal(capture->thread_loop, false);
        return;
    }

    builder = SPA_POD_BUILDER_INIT(parameter_buffer, sizeof(parameter_buffer));

    parameters[count++] = spa_pod_builder_add_object(
        &builder, SPA_TYPE_OBJECT_ParamMeta, SPA_PARAM_Meta,
        SPA_PARAM_META_type, SPA_POD_Id(SPA_META_VideoCrop),
        SPA_PARAM_META_size, SPA_POD_Int(sizeof(struct spa_meta_region)));

    parameters[count++] = spa_pod_builder_add_object(
        &builder, SPA_TYPE_OBJECT_ParamMeta, SPA_PARAM_Meta,
        SPA_PARAM_META_type, SPA_POD_Id(SPA_META_Header),
        SPA_PARAM_META_size, SPA_POD_Int(sizeof(struct spa_meta_header)));

#if PW_CHECK_VERSION(0, 3, 62)
    parameters[count++] = spa_pod_builder_add_object(
        &builder, SPA_TYPE_OBJECT_ParamMeta, SPA_PARAM_Meta,
        SPA_PARAM_META_type, SPA_POD_Id(SPA_META_VideoTransform),
        SPA_PARAM_META_size, SPA_POD_Int(sizeof(struct spa_meta_videotransform)));
#endif

    /* OBS requests MemPtr for its CPU path. MemFd is included because the
     * compositor may allocate SHM itself; MAP_BUFFERS still guarantees data. */
    parameters[count++] = spa_pod_builder_add_object(
        &builder, SPA_TYPE_OBJECT_ParamBuffers, SPA_PARAM_Buffers,
        SPA_PARAM_BUFFERS_dataType,
        SPA_POD_Int((1 << SPA_DATA_MemPtr) | (1 << SPA_DATA_MemFd)));

    if (pw_stream_update_params(capture->stream, parameters, count) < 0) {
        capture->faulted = true;
        report_state(capture, PW_STREAM_STATE_ERROR, "PipeWire rejected buffer parameters");
        pw_thread_loop_signal(capture->thread_loop, false);
        return;
    }

    report_state(capture, PW_STREAM_STATE_PAUSED, "PipeWire RGB format negotiated");
}

static void on_state_changed(void *user_data,
                             enum pw_stream_state previous,
                             enum pw_stream_state state,
                             const char *error)
{
    struct ws_pipewire_stream *capture = user_data;
    (void)previous;
    capture->state = state;
    if (state == PW_STREAM_STATE_ERROR)
        capture->faulted = true;
    report_state(capture, state, error);
    pw_thread_loop_signal(capture->thread_loop, false);
}

static struct pw_buffer *find_latest_buffer(struct pw_stream *stream)
{
    struct pw_buffer *latest = NULL;
    for (;;) {
        struct pw_buffer *candidate = pw_stream_dequeue_buffer(stream);
        if (!candidate)
            return latest;
        if (latest)
            pw_stream_queue_buffer(stream, latest);
        latest = candidate;
    }
}

static void on_process(void *user_data)
{
    struct ws_pipewire_stream *capture = user_data;
    struct pw_buffer *pipewire_buffer = find_latest_buffer(capture->stream);
    struct spa_buffer *buffer;
    struct spa_data *data;
    struct spa_chunk *chunk;
    struct spa_meta_header *header;
    struct spa_meta_region *crop;
    const uint8_t *source;
    uint32_t available;
    uint32_t width;
    uint32_t height;
    uint32_t pixel_format;
    uint64_t crop_offset;
    int32_t stride;

    if (!pipewire_buffer)
        return;

    buffer = pipewire_buffer->buffer;
    if (!buffer || buffer->n_datas != 1)
        goto done;

    header = spa_buffer_find_meta_data(buffer, SPA_META_Header, sizeof(*header));
    if (header && (header->flags & SPA_META_HEADER_FLAG_CORRUPTED))
        goto done;

    data = &buffer->datas[0];
    chunk = data->chunk;
    if (!chunk || !data->data || chunk->size == 0 ||
        (chunk->flags & (SPA_CHUNK_FLAG_CORRUPTED | SPA_CHUNK_FLAG_EMPTY)))
        goto done;
    if (data->type != SPA_DATA_MemPtr && data->type != SPA_DATA_MemFd) {
        if (!capture->unsupported_buffer_reported) {
            capture->unsupported_buffer_reported = true;
            report_state(capture, PW_STREAM_STATE_PAUSED, "Opaque DMA-BUF skipped");
        }
        goto done;
    }
    if (data->maxsize == 0)
        goto done;

    uint32_t offset = chunk->offset % data->maxsize;
    available = SPA_MIN(chunk->size, data->maxsize - offset);
    source = SPA_PTROFF(data->data, offset, const uint8_t);
    stride = chunk->stride ? chunk->stride : (int32_t)(capture->format.size.width * 4u);
    width = capture->format.size.width;
    height = capture->format.size.height;

    crop = spa_buffer_find_meta_data(buffer, SPA_META_VideoCrop, sizeof(*crop));
    if (crop && spa_meta_region_is_valid(crop) && crop->region.size.width > 0 &&
        crop->region.size.height > 0 && crop->region.position.x >= 0 && crop->region.position.y >= 0 &&
        (uint32_t)crop->region.position.x + crop->region.size.width <= width &&
        (uint32_t)crop->region.position.y + crop->region.size.height <= height) {
        crop_offset = (uint64_t)(uint32_t)crop->region.position.y * (uint64_t)(stride < 0 ? -stride : stride) +
                      (uint64_t)(uint32_t)crop->region.position.x * 4u;
        if (crop_offset < available) {
            source += crop_offset;
            available -= (uint32_t)crop_offset;
            width = crop->region.size.width;
            height = crop->region.size.height;
        }
    }

    if (map_pixel_format(capture->format.format, &pixel_format) && capture->frame_callback)
        capture->frame_callback(capture->user_data, source, available, stride, width, height, pixel_format);

done:
    pw_stream_queue_buffer(capture->stream, pipewire_buffer);
}

static const struct pw_stream_events stream_events = {
    PW_VERSION_STREAM_EVENTS,
    .state_changed = on_state_changed,
    .param_changed = on_parameter_changed,
    .process = on_process,
};

static bool wait_for_condition(struct ws_pipewire_stream *capture,
                               bool (*condition)(const struct ws_pipewire_stream *))
{
    struct timespec deadline;
    if (pw_thread_loop_get_time(capture->thread_loop, &deadline, WS_WAIT_TIMEOUT_NS) < 0)
        return false;
    while (!condition(capture) && !capture->faulted) {
        int result = pw_thread_loop_timed_wait_full(capture->thread_loop, &deadline);
        if (result == -ETIMEDOUT)
            return false;
        if (result < 0 && result != -EINTR)
            return false;
    }
    return condition(capture) && !capture->faulted;
}

static bool core_is_ready(const struct ws_pipewire_stream *capture)
{
    return capture->core_synchronized;
}

static bool stream_is_ready(const struct ws_pipewire_stream *capture)
{
    return capture->state == PW_STREAM_STATE_STREAMING;
}

static void destroy_capture(struct ws_pipewire_stream *capture)
{
    if (!capture)
        return;

    if (capture->thread_loop && capture->loop_started)
        pw_thread_loop_lock(capture->thread_loop);
    if (capture->stream) {
        pw_stream_disconnect(capture->stream);
        pw_stream_destroy(capture->stream);
        capture->stream = NULL;
    }
    if (capture->core) {
        pw_core_disconnect(capture->core);
        capture->core = NULL;
    }
    if (capture->thread_loop && capture->loop_started)
        pw_thread_loop_unlock(capture->thread_loop);
    if (capture->thread_loop && capture->loop_started) {
        pw_thread_loop_stop(capture->thread_loop);
        capture->loop_started = false;
    }
    if (capture->context)
        pw_context_destroy(capture->context);
    if (capture->thread_loop)
        pw_thread_loop_destroy(capture->thread_loop);
    runtime_release();
    free(capture);
}

WS_EXPORT struct ws_pipewire_stream *ws_pipewire_stream_create(
    int portal_file_descriptor,
    uint32_t node_id,
    uint32_t target_width,
    uint32_t target_height,
    uint32_t maximum_framerate,
    ws_frame_callback frame_callback,
    ws_state_callback state_callback,
    void *user_data)
{
    struct ws_pipewire_stream *capture;
    struct spa_pod_builder builder;
    const struct spa_pod *parameters[4];
    uint8_t parameter_buffer[4096];
    struct pw_properties *properties;
    uint32_t parameter_count;
    int duplicated_file_descriptor;

    if (portal_file_descriptor < 0 || target_width == 0 || target_height == 0 ||
        maximum_framerate == 0 || !frame_callback)
        return NULL;

    capture = calloc(1, sizeof(*capture));
    if (!capture)
        return NULL;
    runtime_acquire();

    capture->frame_callback = frame_callback;
    capture->state_callback = state_callback;
    capture->user_data = user_data;
    capture->target_width = target_width;
    capture->target_height = target_height;
    capture->maximum_framerate = maximum_framerate;
    capture->state = PW_STREAM_STATE_UNCONNECTED;

    capture->thread_loop = pw_thread_loop_new("WindowSwitcher PipeWire", NULL);
    if (!capture->thread_loop)
        goto error;
    capture->context = pw_context_new(pw_thread_loop_get_loop(capture->thread_loop), NULL, 0);
    if (!capture->context || pw_thread_loop_start(capture->thread_loop) < 0)
        goto error;
    capture->loop_started = true;

    duplicated_file_descriptor = fcntl(portal_file_descriptor, F_DUPFD_CLOEXEC, 5);
    if (duplicated_file_descriptor < 0)
        goto error;

    pw_thread_loop_lock(capture->thread_loop);
    capture->core = pw_context_connect_fd(capture->context, duplicated_file_descriptor, NULL, 0);
    if (!capture->core) {
        close(duplicated_file_descriptor);
        pw_thread_loop_unlock(capture->thread_loop);
        goto error;
    }
    pw_core_add_listener(capture->core, &capture->core_listener, &core_events, capture);
    capture->sync_sequence = pw_core_sync(capture->core, PW_ID_CORE, 0);
    if (capture->sync_sequence < 0 || !wait_for_condition(capture, core_is_ready)) {
        pw_thread_loop_unlock(capture->thread_loop);
        goto error;
    }

    properties = pw_properties_new(PW_KEY_MEDIA_TYPE, "Video",
                                   PW_KEY_MEDIA_CATEGORY, "Capture",
                                   PW_KEY_MEDIA_ROLE, "Screen",
                                   NULL);
    capture->stream = pw_stream_new(capture->core, "WindowSwitcher preview", properties);
    if (!capture->stream) {
        pw_thread_loop_unlock(capture->thread_loop);
        goto error;
    }
    pw_stream_add_listener(capture->stream, &capture->stream_listener, &stream_events, capture);

    builder = SPA_POD_BUILDER_INIT(parameter_buffer, sizeof(parameter_buffer));
    parameter_count = build_format_parameters(capture, &builder, parameters, SPA_N_ELEMENTS(parameters));
    if (parameter_count == 0 ||
        pw_stream_connect(capture->stream,
                          PW_DIRECTION_INPUT,
                          node_id,
                          PW_STREAM_FLAG_AUTOCONNECT | PW_STREAM_FLAG_MAP_BUFFERS,
                          parameters,
                          parameter_count) < 0 ||
        !wait_for_condition(capture, stream_is_ready)) {
        pw_thread_loop_unlock(capture->thread_loop);
        goto error;
    }
    pw_thread_loop_unlock(capture->thread_loop);
    return capture;

error:
    destroy_capture(capture);
    return NULL;
}

WS_EXPORT int ws_pipewire_stream_set_active(struct ws_pipewire_stream *capture, int active)
{
    int result;
    if (!capture || !capture->stream || !capture->thread_loop)
        return -EINVAL;
    pw_thread_loop_lock(capture->thread_loop);
    result = pw_stream_set_active(capture->stream, active != 0);
    pw_thread_loop_unlock(capture->thread_loop);
    return result;
}

WS_EXPORT int ws_pipewire_stream_update_target(struct ws_pipewire_stream *capture,
                                               uint32_t width,
                                               uint32_t height)
{
    struct spa_pod_builder builder;
    const struct spa_pod *parameters[4];
    uint8_t parameter_buffer[4096];
    uint32_t parameter_count;
    int result;

    if (!capture || !capture->stream || width == 0 || height == 0)
        return -EINVAL;

    pw_thread_loop_lock(capture->thread_loop);
    capture->target_width = width;
    capture->target_height = height;
    builder = SPA_POD_BUILDER_INIT(parameter_buffer, sizeof(parameter_buffer));
    parameter_count = build_format_parameters(capture, &builder, parameters, SPA_N_ELEMENTS(parameters));
    result = parameter_count > 0 ? pw_stream_update_params(capture->stream, parameters, parameter_count) : -ENOSPC;
    pw_thread_loop_unlock(capture->thread_loop);
    return result;
}

WS_EXPORT int ws_pipewire_stream_is_faulted(const struct ws_pipewire_stream *capture)
{
    return !capture || capture->faulted;
}

WS_EXPORT void ws_pipewire_stream_destroy(struct ws_pipewire_stream *capture)
{
    destroy_capture(capture);
}
