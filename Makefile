SOLUTION := Window-Switcher.slnx
APP_PROJECT := src/WindowSwitcher/WindowSwitcher.csproj
TEST_PROJECT := src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj
BUILD_SCRIPT := ./build/build.sh
BUILD_CMD := ./build/build.cmd
SENTRY_TELEMETRY ?= true
MSBUILD_SENTRY_PROPERTY := -p:EnableSentryTelemetry=$(SENTRY_TELEMETRY)
NUKE_SENTRY_ARGUMENT := --enable-sentry-telemetry $(SENTRY_TELEMETRY)

ifeq ($(OS),Windows_NT)
NUKE := $(BUILD_CMD)
else
NUKE := $(BUILD_SCRIPT)
endif

.PHONY: help restore build run test test-no-build clean format format-check artifacts installer appimage

help:
	@echo "Window Switcher development commands"
	@echo ""
	@echo "  make restore       Restore NuGet packages and local .NET tools"
	@echo "  make build         Build the solution"
	@echo "  make run           Run the desktop app"
	@echo "  make test          Run the test project"
	@echo "  make test-no-build Run tests without rebuilding"
	@echo "  make clean         Clean the solution"
	@echo "  make format        Format source with CSharpier"
	@echo "  make format-check  Check source formatting with CSharpier"
	@echo "  make artifacts     Build host-specific release artifacts with Nuke"
	@echo "  make installer     Build the Windows installer with Nuke (Windows only)"
	@echo "  make appimage      Build the Linux AppImage with Nuke (Linux only)"
	@echo ""
	@echo "Set SENTRY_TELEMETRY=false to compile without Sentry."

restore:
	dotnet restore $(SOLUTION) $(MSBUILD_SENTRY_PROPERTY)
	dotnet tool restore

build:
	dotnet build $(SOLUTION) $(MSBUILD_SENTRY_PROPERTY)

run:
	dotnet run --project $(APP_PROJECT) $(MSBUILD_SENTRY_PROPERTY)

test:
	dotnet test $(TEST_PROJECT) $(MSBUILD_SENTRY_PROPERTY)

test-no-build:
	dotnet test $(TEST_PROJECT) --no-build

clean:
	dotnet clean $(SOLUTION)

format:
	dotnet tool restore
	dotnet csharpier .

format-check:
	dotnet tool restore
	dotnet csharpier . --check

artifacts:
	$(NUKE) --target Artifacts $(NUKE_SENTRY_ARGUMENT)

installer:
ifeq ($(OS),Windows_NT)
	$(BUILD_CMD) --target Installer $(NUKE_SENTRY_ARGUMENT)
else
	@echo "The Windows installer can only be built on Windows."
	@echo "On Linux, use 'make appimage' or 'make artifacts' to build the AppImage."
	@exit 2
endif

appimage:
ifeq ($(OS),Windows_NT)
	@echo "The Linux AppImage can only be built on Linux."
	@exit 2
else
	$(BUILD_SCRIPT) --target AppImage $(NUKE_SENTRY_ARGUMENT)
endif
