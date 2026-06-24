SOLUTION := Window-Switcher.slnx
APP_PROJECT := src/WindowSwitcher/WindowSwitcher.csproj
TEST_PROJECT := src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj
BUILD_SCRIPT := ./build/build.sh
BUILD_CMD := ./build/build.cmd

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
	@echo "  make installer     Build the Windows installer with Nuke"
	@echo "  make appimage      Build the Linux AppImage with Nuke"

restore:
	dotnet restore $(SOLUTION)
	dotnet tool restore

build:
	dotnet build $(SOLUTION)

run:
	dotnet run --project $(APP_PROJECT)

test:
	dotnet test $(TEST_PROJECT)

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
	@if command -v sh >/dev/null 2>&1; then \
		$(BUILD_SCRIPT) --target Artifacts; \
	else \
		$(BUILD_CMD) --target Artifacts; \
	fi

installer:
	$(BUILD_CMD) --target Installer

appimage:
	$(BUILD_SCRIPT) --target AppImage
