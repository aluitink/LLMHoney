# LLMHoney Project

This is a honeypot solution that uses Large Language Models (LLMs) to generate realistic responses to network intrusions and attacks.

## Core Concept
- **Honeypot**: Network security tool that simulates vulnerable services to attract and analyze attackers
- **LLM Integration**: Uses Azure OpenAI via Semantic Kernel to generate context-aware, realistic responses
- **Multi-Protocol Support**: Simulates various services (SSH, FTP, HTTP, Telnet) through configurable TCP listeners
- **Security Research**: Captures and analyzes attack patterns for threat intelligence

## Key Components
- `LLMHoney.Host`: Main service with multi-socket listeners
- `LLMHoney.Core`: Message capture and truncation analysis utilities  
- `LLMHoney.Llm.*`: LLM abstraction layer and Azure OpenAI integration

## Development Focus
When working on this project, prioritize:
- Network security and protocol simulation accuracy
- LLM response quality and realism
- Performance and resource management
- Comprehensive logging and analysis capabilities
