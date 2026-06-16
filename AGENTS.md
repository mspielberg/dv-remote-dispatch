# AGENTS.md

## Repository Layout
This is a C# UnityModManager mod for Derail Valley that provides remote dispatch capabilities. The codebase is structured as follows:
- `RemoteDispatch/` - Main mod source code
- `RemoteDispatch.Signals/` - Signals integration module 
- `RemoteDispatch.Tests/` - Unit tests
- `RemoteDispatch.Signals.Tests/` - Signals tests
- `build/` - Build output directory
- `dist/` - Distribution package directory
- `frontend/` - Embedded web assets (HTML, JS, CSS)

## File Descriptions

### Main Mod Files
- `Main.cs` - Entry point and mod lifecycle management
- `Settings.cs` - Configuration and permissions handling  
- `HttpServer.cs` - HTTP endpoint implementation and request handlers
- `CarData.cs` - Car/locomotive data processing
- `PlayerData.cs` - Player blip data processing
- `RailTracks.cs` - Track and junction data processing

### Signals Module
- `Bootstrap.cs` - Signals integration initialization
- `SignalsBridge.cs` - Communication bridge with Signals API
- `LoggingReturn.cs` - Logging callbacks for signals module

### Test Files
- `UnitTest1.cs` - Basic unit test placeholder
- `Bootstrap.cs` - Signals tests (missing in current structure)

## Code Flow
The mod initializes through Main.Load(), then patches game systems via Harmony. On enable, it starts HTTP server and data updaters. Data is served through HTTP endpoints:
- `/car` - Car/locomotive data
- `/player` - Player blip data  
- `/track` - Track junction data
- `/signals` - Signal data (when enabled)
- `/updates` - Real-time data updates

## Build/Lint/Test Commands
```bash
# Build the solution
dotnet build RemoteDispatch.slnx

# Run tests 
dotnet test RemoteDispatch.Tests/RemoteDispatch.Tests.csproj

# Single test execution  
dotnet test RemoteDispatch.Tests/RemoteDispatch.Tests.csproj --filter "Test1"

# Package for release
.\package.ps1 -Configuration Release

# Deploy for development  
.\package.ps1 -Configuration Debug -DVPath "C:\path\to\derailvalley"
```

## Code Style Guidelines

### Imports
- Group using statements with standard libraries first, then third-party, then project-specific
- Use full namespaces for clarity (no 'using static' except where explicitly needed)
- Organize imports alphabetically within groups

### Formatting
- Use 4-space indentation (no tabs)
- Follow C# naming conventions (PascalCase for methods and properties, camelCase for parameters)
- Place opening braces on same line as control statements
- Use blank lines to separate logical sections

### Types
- Prefer readonly fields over constants when possible  
- Use var for local variables with explicit types
- Prefer explicit null checking over null-conditional operators where clarity is preferred
- Use nullable reference types (enabled in project)

### Naming Conventions
- Classes: PascalCase
- Methods: PascalCase
- Variables: camelCase  
- Constants: PascalCase
- Private fields: _camelCase

### Error Handling
- Use try/catch blocks around potentially failing operations
- Log errors with descriptive messages including stack traces when appropriate
- Prefer specific exception handling over generic catch-all blocks
- Handle null values gracefully with explicit checks

### Documentation
- Document public methods with XML comments
- Use TODO comments for incomplete features
- Keep inline comments brief and focused

### Syntax Safety Checklist
- **NEVER** use replaceAll on multi-line spans without precise boundary matching
- Always verify there are no duplicate code blocks after editing
- When replacing text, include enough surrounding context to make it unique
- After any edit, read the affected file to confirm syntax integrity
