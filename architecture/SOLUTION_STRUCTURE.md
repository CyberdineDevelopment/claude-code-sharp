# Claude Code Sharp - Solution Architecture

## Solution Structure

```
ClaudeCode.sln
├── src/
│   ├── ClaudeCode.CLI/                          # Main CLI application
│   ├── ClaudeCode.Core/                         # Core domain models and abstractions
│   ├── ClaudeCode.MCP/                          # MCP protocol implementation
│   ├── ClaudeCode.MCP.Tools/                    # Built-in MCP tools
│   ├── ClaudeCode.PowerShell/                   # PowerShell host integration
│   ├── ClaudeCode.Security/                     # Security and permissions framework
│   ├── ClaudeCode.Configuration/                # Configuration management
│   ├── ClaudeCode.Context/                      # Context and session management
│   ├── ClaudeCode.Roslyn/                       # Roslyn-based language services
│   └── ClaudeCode.Extensibility/                # Plugin and extensibility framework
├── tools/
│   ├── ClaudeCode.Tools.CSharp/                 # C# specific MCP tools
│   ├── ClaudeCode.Tools.Git/                    # Git integration tools
│   ├── ClaudeCode.Tools.FileSystem/             # File system operation tools
│   └── ClaudeCode.Tools.Testing/                # Testing framework tools
├── tests/
│   ├── ClaudeCode.Core.Tests/
│   ├── ClaudeCode.MCP.Tests/
│   ├── ClaudeCode.PowerShell.Tests/
│   ├── ClaudeCode.Security.Tests/
│   └── ClaudeCode.Integration.Tests/
└── samples/
    ├── CustomTool.Sample/                       # Sample custom tool implementation
    └── Plugin.Sample/                           # Sample plugin implementation
```

## Project Dependencies

### Core Layer
- **ClaudeCode.Core**: No dependencies (pure domain)
- **ClaudeCode.Configuration**: → Core
- **ClaudeCode.Security**: → Core, Configuration

### Infrastructure Layer
- **ClaudeCode.MCP**: → Core, Security
- **ClaudeCode.PowerShell**: → Core, Security, Configuration
- **ClaudeCode.Context**: → Core, Configuration, Security
- **ClaudeCode.Roslyn**: → Core, Security

### Tool Layer
- **ClaudeCode.MCP.Tools**: → Core, MCP, Security
- **ClaudeCode.Tools.*****: → Core, MCP, MCP.Tools

### Application Layer
- **ClaudeCode.Extensibility**: → Core, MCP, Security, Configuration
- **ClaudeCode.CLI**: → All projects

## Technology Stack

- **.NET 10**: Latest framework features and performance
- **C# 13**: Modern language features (primary constructors, collection expressions)
- **Microsoft.Extensions.Hosting**: For hosted services and DI
- **Microsoft.Extensions.Configuration**: Configuration management
- **Microsoft.Extensions.Logging**: Structured logging
- **System.Management.Automation**: PowerShell hosting
- **Microsoft.CodeAnalysis**: Roslyn analyzers and language services
- **System.Text.Json**: JSON serialization for MCP protocol
- **Microsoft.Extensions.Options**: Strongly-typed configuration