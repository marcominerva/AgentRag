# Agent RAG

Agent RAG is a .NET 10 console app that showcases a simple Retrieval-Augmented Generation (RAG) workflow built with Microsoft Agents AI and OpenAI. The application reformulates the user question, performs a contextual search on local sample data, and streams the final answer back to the console with source-aware context.

## What it does

- Uses an OpenAI chat client through `Microsoft.Agents.AI.OpenAI`
- Reformulates user questions before retrieval
- Injects contextual results through a `TextSearchProvider`
- Streams the final response in the console
- Includes sample in-memory knowledge entries for topics such as Taggia, the Moon, Mars, and Pluto

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An Azure OpenAI resource or compatible OpenAI endpoint
- A deployed chat model such as `gpt-4.1`

## Configuration

The project currently reads its configuration from `AgentRag/Constants.cs`.

Update the following values before running the app:

- `Endpoint`: your Azure OpenAI endpoint
- `DeploymentName`: the name of your deployed chat model
- `ApiKey`: your API key

Example:

```csharp
public static class Constants
{
    public const string Endpoint = "https://your-resource.openai.azure.com/openai/v1/";
    public const string DeploymentName = "gpt-4.1";
    public const string ApiKey = "your-api-key";
}
```

> [!IMPORTANT]
> Do not commit real secrets to source control. For real projects, prefer environment variables, user secrets, or another secure secret store instead of hardcoded credentials.

## Run the project

From the repository root:

```bash
dotnet restore
dotnet run --project AgentRag/AgentRag.csproj
```

When the app starts, type a question in the console prompt:

```text
Question: What do you know about Mars?
```

## Notes

- The current search implementation is a simple in-memory demo provider.
- The app is intended as a minimal sample to explore question reformulation, retrieval, and streaming responses in a console-based RAG experience.

## Architecture

The application is organized around two agents and one contextual search provider:

- **Reformulation agent**: rewrites the user question using the current chat context so retrieval works better, while preserving the original language.
- **RAG agent**: receives the reformulated question, uses retrieved context, and generates the final answer.
- **Search provider**: returns matching `TextSearchResult` items from an in-memory dataset used as demo knowledge.

High-level flow:

1. The user enters a question in the console.
2. The reformulation agent rewrites the question for retrieval.
3. The search provider returns relevant contextual snippets.
4. The main agent answers using only the provided context.
5. The response is streamed back to the console.

Key implementation pieces:

- `Program.cs`: application setup, agent creation, session handling, and console loop
- `SearchProvider`: demo retrieval layer backed by in-memory sample data
- `TraceHttpClientHandler`: traces outgoing HTTP request payloads for inspection
- `Constants.cs`: endpoint, deployment, and API key configuration

## Sample output

Example session:

```text
Question: What do you know about Mars?
Mars is the fourth planet from the Sun and is a terrestrial planet. It is often called the red planet because of the large amount of iron oxide on its surface. It has a thin atmosphere, polar ice caps, volcanoes, valleys, and evidence of water ice. Its two natural satellites are Phobos and Deimos.

Question: How long is a day on Mars?
The solar day on Mars, called a Sol, lasts 24 hours, 37 minutes, and 23 seconds.
```

Example of an out-of-scope answer:

```text
Question: Who wrote The Divine Comedy?
I don't have that information in the given context. Please refine the question based on the available content.
```
