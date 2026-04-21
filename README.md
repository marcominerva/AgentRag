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
