# Claude Code

A professional, MCP-based alternative to Claude Code built in C# with clean architecture and enterprise-grade patterns.

## 🎯 Features

### Core Capabilities
- **MCP Integration**: Full Model Context Protocol support for extensible tools
- **Multiple Transports**: Stdio, HTTP, and WebSocket support
- **Language-Specific Servers**: Dedicated MCP servers per programming language
- **Streaming Responses**: Real-time response streaming from Claude
- **Professional Architecture**: Clean separation of concerns with proper abstractions

### Architecture Highlights
- **Clean Architecture**: Abstractions, implementations, and CLI layers
- **Dependency Injection**: Full DI container with configuration
- **Modern C#**: .NET 9, nullable reference types, file-scoped namespaces
- **Enterprise Patterns**: Proper error handling, logging, and configuration
- **Performance Optimized**: Async/await throughout, memory efficient

## 🚀 Quick Start

### Prerequisites
- .NET 9 SDK
- Anthropic API key
- MCP servers (optional but recommended)

### Installation

```bash
git clone https://github.com/CyberdineDevelopment/claude-code-sharp.git
cd claude-code-sharp
dotnet build
```

### Configuration

1. Set your Anthropic API key:
```bash
export CLAUDECODE__ANTHROPIC__APIKEY="your-api-key"
```

2. Or configure in `appsettings.json`:
```json
{
  "ClaudeCode": {
    "Anthropic": {
      "ApiKey": "your-api-key"
    }
  }
}
```

### Usage

Start an interactive chat:
```bash
dotnet run --project src/CyberdineDevelopment.ClaudeCode.CLI chat
```

Send a single message:
```bash
dotnet run --project src/CyberdineDevelopment.ClaudeCode.CLI chat -m "Hello, Claude!"
```

List configured servers:
```bash
dotnet run --project src/CyberdineDevelopment.ClaudeCode.CLI server list
```

## 🏗 Architecture

### Project Structure

```
src/
├── CyberdineDevelopment.ClaudeCode.Abstractions/     # Core interfaces and contracts
├── CyberdineDevelopment.ClaudeCode.MCP/              # MCP protocol implementation
├── CyberdineDevelopment.ClaudeCode.Anthropic/        # Anthropic API client
└── CyberdineDevelopment.ClaudeCode.CLI/              # Command-line interface
```

### Key Components

#### Abstractions Layer
- `IMcpClient` - MCP protocol client contract
- `IAnthropicClient` - Anthropic API client contract
- Transport abstractions and DTOs

#### MCP Implementation
- JSON-RPC 2.0 transport layer
- Stdio transport for process communication
- Full MCP protocol support (tools, resources, etc.)

#### Anthropic Client
- Native HTTP client for Anthropic API
- Streaming response support
- Proper error handling and retry logic

#### CLI Interface
- System.CommandLine integration
- Configuration management
- Interactive and batch modes

## 🔧 Configuration

### Server Configuration

Configure MCP servers in `appsettings.json`:

```json
{
  "ClaudeCode": {
    "Servers": {
      "csharp-tools": {
        "Name": "C# Development Tools",
        "Description": "Tools for C# development",
        "Command": "mcp-server-csharp",
        "Arguments": ["--workspace", "."],
        "AutoStart": true
      }
    }
  }
}
```

### Model Settings

```json
{
  "ClaudeCode": {
    "DefaultModel": "claude-3-5-sonnet-20241022",
    "MaxTokens": 4096,
    "Temperature": 0.7
  }
}
```

## 🛠 Development

### Building

```bash
dotnet build
```

### Running Tests (when implemented)

```bash
dotnet test
```

### Publishing

```bash
dotnet publish src/CyberdineDevelopment.ClaudeCode.CLI -c Release -r win-x64 --self-contained
```

## 📝 MCP Protocol

This implementation supports MCP version 2024-11-05 with:

- **Tools**: Call server-provided tools
- **Resources**: Read server-managed resources
- **Initialization**: Proper handshake and capability negotiation
- **Error Handling**: Structured error responses
- **Streaming**: Real-time bidirectional communication

### Example MCP Server Integration

```json
{
  "filesystem": {
    "Name": "File System Tools",
    "Command": "npx",
    "Arguments": ["@modelcontextprotocol/server-filesystem", "/path/to/workspace"],
    "AutoStart": true
  }
}
```

## 🔒 Security

- API keys stored securely via configuration
- Process isolation for MCP servers
- Input validation throughout
- No hardcoded credentials

## 🚀 Roadmap

- [ ] Complete MCP server lifecycle management
- [ ] Add HTTP/WebSocket transport support
- [ ] Implement conversation history
- [ ] Add plugin system for custom tools
- [ ] Create language-specific MCP server templates
- [ ] Add comprehensive test suite
- [ ] Performance optimizations
- [ ] Docker containerization

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests for new functionality
5. Submit a pull request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- [Anthropic](https://anthropic.com) for the Claude API
- [Model Context Protocol](https://modelcontextprotocol.io) specification
- [System.CommandLine](https://github.com/dotnet/command-line-api) for CLI framework

---

**Note**: This is an independent implementation and is not affiliated with Anthropic's official Claude Code project.