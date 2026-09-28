SOLUTION := Window-Switcher.slnx
APP_PROJECT := src/WindowSwitcher/WindowSwitcher.csproj
TEST_PROJECT := src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj
SENTRY_TELEMETRY ?= true
TELEMETRY_DISTRIBUTION_CHANNEL ?= source
TELEMETRY_PACKAGE_KIND ?= unpackaged
MSBUILD_SENTRY_PROPERTY := -p:EnableSentryTelemetry=$(SENTRY_TELEMETRY) -p:TelemetryDistributionChannel=$(TELEMETRY_DISTRIBUTION_CHANNEL) -p:TelemetryPackageKind=$(TELEMETRY_PACKAGE_KIND)
NUKE_SENTRY_ARGUMENT := --enable-sentry-telemetry $(SENTRY_TELEMETRY)
NUKE_TELEMETRY_ARGUMENTS := $(NUKE_SENTRY_ARGUMENT) --distribution-channel $(TELEMETRY_DISTRIBUTION_CHANNEL) --package-kind $(TELEMETRY_PACKAGE_KIND)
BUILD_SCRIPT := ./build/build.sh

.PHONY: help restore build run test test-no-build clean format format-check artifacts appimage

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
	@echo "  make appimage      Build the Linux AppImage with Nuke"
	@echo ""
	@echo "Set SENTRY_TELEMETRY=false to compile without Sentry."
	@echo "Set TELEMETRY_DISTRIBUTION_CHANNEL and TELEMETRY_PACKAGE_KIND to label packaged builds."

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
	dotnet csharpier format .

format-check:
	dotnet tool restore
	dotnet csharpier check .

appimage:
	$(BUILD_SCRIPT) --target AppImage $(NUKE_TELEMETRY_ARGUMENTS)
