using WindowSwitcher.Lib.Models;
using WindowSwitcher.ViewModels;
using WindowSwitcher.ViewModels.Abstractions;
using Xunit;

namespace WindowSwitcher.Tests.Windows;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void PreviewHighlightColor_UpdatesRepositoryAndInvokesApplyCallback()
    {
        var repository = new FakeSettingsRepository();
        string appliedColor = string.Empty;
        var sut = new SettingsViewModel(repository, () => { }, color => appliedColor = color);

        sut.PreviewHighlightColor = "  #112233  ";

        Assert.Equal("#112233", repository.Config.PreviewHighlightColor);
        Assert.Equal("#112233", appliedColor);
    }

    [Fact]
    public void EnablePreviews_AppliesImmediatelyAndInvokesApplyAction()
    {
        var repository = new FakeSettingsRepository();
        repository.Config.EnablePreviews = false;
        int applyActionInvocationCount = 0;
        var sut = new SettingsViewModel(repository, () => applyActionInvocationCount++, _ => { });

        Assert.False(repository.Config.EnablePreviews);

        sut.EnablePreviews = true;

        Assert.True(repository.Config.EnablePreviews);
        Assert.Equal(1, applyActionInvocationCount);
    }

    [Fact]
    public void HideActivePreview_UpdatesRepositoryAndInvokesApplyAction()
    {
        var repository = new FakeSettingsRepository();
        int applyActionInvocationCount = 0;
        var sut = new SettingsViewModel(repository, () => applyActionInvocationCount++, _ => { });

        Assert.False(sut.HideActivePreview);

        sut.HideActivePreview = true;

        Assert.True(repository.Config.HideActivePreview);
        Assert.Equal(1, applyActionInvocationCount);
    }

    /// <summary>
    /// Every setting: the value to write, and whether open preview windows must be updated.
    /// </summary>
    public static TheoryData<string, object, bool> Settings =>
        new()
        {
            { nameof(SettingsViewModel.EnablePreviews), false, true },
            { nameof(SettingsViewModel.HideActivePreview), true, true },
            { nameof(SettingsViewModel.StartMinimized), true, false },
            { nameof(SettingsViewModel.ResizeWindows), false, true },
            { nameof(SettingsViewModel.MoveWindows), false, false },
            { nameof(SettingsViewModel.FocusOnHover), true, false },
            { nameof(SettingsViewModel.UseFixedWindowSize), true, true },
            { nameof(SettingsViewModel.WindowWidth), 640, true },
            { nameof(SettingsViewModel.WindowHeight), 480, true },
        };

    [Theory]
    [MemberData(nameof(Settings))]
    public void Setting_IsPersistedNotifiedAndAppliedOnlyWhenItChanges(
        string propertyName,
        object newValue,
        bool appliesToWindows
    )
    {
        var repository = new FakeSettingsRepository();
        int applyCount = 0;
        var sut = new SettingsViewModel(repository, () => applyCount++, _ => { });
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        var viewModelProperty = typeof(SettingsViewModel).GetProperty(propertyName)!;
        var configProperty = typeof(ConfigFile).GetProperty(propertyName)!;
        Assert.NotEqual(newValue, configProperty.GetValue(repository.Config));

        viewModelProperty.SetValue(sut, newValue);
        viewModelProperty.SetValue(sut, newValue);

        Assert.Equal(newValue, configProperty.GetValue(repository.Config));
        Assert.Equal(newValue, viewModelProperty.GetValue(sut));
        Assert.Equal(1, changed.Count(name => name == propertyName));
        Assert.Equal(appliesToWindows ? 1 : 0, applyCount);
    }

    [Fact]
    public void UseFixedWindowSize_DisablesResizeWindowsEditing()
    {
        var repository = new FakeSettingsRepository();
        var sut = new SettingsViewModel(repository, () => { }, _ => { });
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        Assert.True(sut.CanEditResizeWindows);

        sut.UseFixedWindowSize = true;

        Assert.False(sut.CanEditResizeWindows);
        Assert.Contains(nameof(SettingsViewModel.CanEditResizeWindows), changed);
    }

    [Fact]
    public void UsePerPreviewSize_IsTheInverseOfUseFixedWindowSize()
    {
        var repository = new FakeSettingsRepository();
        var sut = new SettingsViewModel(repository, () => { }, _ => { });
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);
        Assert.True(sut.UsePerPreviewSize);

        sut.UsePerPreviewSize = false;

        Assert.True(repository.Config.UseFixedWindowSize);
        Assert.True(sut.UseFixedWindowSize);
        Assert.False(sut.UsePerPreviewSize);
        Assert.Contains(nameof(SettingsViewModel.UsePerPreviewSize), changed);

        sut.UsePerPreviewSize = true;

        Assert.False(repository.Config.UseFixedWindowSize);
    }

    [Fact]
    public void Reload_NotifiesEverySettingAndAppliesTheConfiguration()
    {
        var repository = new FakeSettingsRepository();
        repository.Config.PreviewHighlightColor = "#112233";
        int applyCount = 0;
        string appliedColor = string.Empty;
        var sut = new SettingsViewModel(
            repository,
            () => applyCount++,
            color => appliedColor = color
        );
        var changed = new List<string?>();
        sut.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        sut.Reload();

        Assert.Contains(string.Empty, changed);
        Assert.Equal(1, applyCount);
        Assert.Equal("#112233", appliedColor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void PreviewHighlightColor_FallsBackToTheDefaultColour(string? value)
    {
        var repository = new FakeSettingsRepository();
        repository.Config.PreviewHighlightColor = "#112233";
        var sut = new SettingsViewModel(repository, () => { }, _ => { });

        sut.PreviewHighlightColor = value!;

        Assert.Equal("#E3008C", repository.Config.PreviewHighlightColor);
        Assert.Equal("#E3008C", sut.PreviewHighlightColor);
    }

    [Fact]
    public void PreviewHighlightColor_IgnoresACaseOnlyChange()
    {
        var repository = new FakeSettingsRepository();
        repository.Config.PreviewHighlightColor = "#AABBCC";
        int applyCount = 0;
        var sut = new SettingsViewModel(repository, () => applyCount++, _ => applyCount++);

        sut.PreviewHighlightColor = "#aabbcc";

        Assert.Equal("#AABBCC", repository.Config.PreviewHighlightColor);
        Assert.Equal(0, applyCount);
    }

    internal sealed class FakeSettingsRepository : ISettingsRepository
    {
        public ConfigFile Config { get; } = new();

        public T Read<T>(Func<ConfigFile, T> reader)
        {
            return reader(Config);
        }

        public void Update(Action<ConfigFile> update)
        {
            update(Config);
        }
    }
}
