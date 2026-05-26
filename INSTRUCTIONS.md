# WhiskeyDistiller Usage Instructions

This guide explains how to connect and run **WhiskeyDistiller** for both local MCP configurations and non-MCP (GitHub Copilot Custom Tools) setups.

---

## 1. Personal Setup (Model Context Protocol - MCP)

Use this method to connect your search engine directly to local AI agents (like Cursor, Claude Code, or Windsurf) via stdio MCP.

### Step 1: Compile the MCP Server
First, build the MCP console app in Release mode:
```bash
cd c:/Users/rioca/OneDrive/Desktop/Projects/WikiProject/Whiskey/whiskey-distiller
dotnet build -c Release
```
This compiles the binary to:
`c:/Users/rioca/OneDrive/Desktop/Projects/WikiProject/Whiskey/whiskey-distiller/WhiskeyDistiller.Mcp/bin/Release/net10.0/WhiskeyDistiller.Mcp.dll`

### Step 2: Configure your IDE Client

#### A. For Cursor
1. Go to **Cursor Settings** -> **Features** -> **MCP**.
2. Click **+ Add New MCP Server**.
3. Fill in the details:
   *   **Name**: `WhiskeyDistiller`
   *   **Type**: `command`
   *   **Command**: `dotnet`
   *   **Arguments**: `c:/Users/rioca/OneDrive/Desktop/Projects/WikiProject/Whiskey/whiskey-distiller/WhiskeyDistiller.Mcp/bin/Release/net10.0/WhiskeyDistiller.Mcp.dll`
4. Click **Save**. Cursor will start the server, download the model files, and index your folder.

#### B. For Claude Code
Run the following command to add the server:
```bash
claude mcp add whiskey-distiller -- dotnet c:/Users/rioca/OneDrive/Desktop/Projects/WikiProject/Whiskey/whiskey-distiller/WhiskeyDistiller.Mcp/bin/Release/net10.0/WhiskeyDistiller.Mcp.dll
```

---

## 2. Shell Command Search (Sub-Agent Setup)

For sub-agents (like Claude Code's sub-agents or Cursor terminal runners) that cannot use MCP, you can invoke the search engine directly from the command line using the `--search` argument.

You can specify a query and optionally pass the workspace folder path you want to index and search:
```bash
dotnet c:/Users/rioca/OneDrive/Desktop/Projects/WikiProject/Whiskey/whiskey-distiller/WhiskeyDistiller.Mcp/bin/Release/net10.0/WhiskeyDistiller.Mcp.dll --search "your search terms" [optional_path_to_workspace]
```

*   **`[optional_path_to_workspace]`**: The path to the folder you want to search. If omitted, the command defaults to searching the current working directory of the shell.

---

## 3. Non-MCP Setup (GitHub Copilot Custom Tools Integration)

For environments where local MCP is not available or blocked, configure your hosted WhiskeyDistiller API server as a **GitHub Copilot Custom Tool** (Skillset). Copilot calls your REST API directly from the cloud using its OpenAPI specification.

### Step 1: Deploy in Docker
Run the container in your company's hosting environment or corporate server:
```bash
cd c:/Users/rioca/OneDrive/Desktop/Projects/WikiProject/Whiskey/whiskey-distiller
docker compose up -d
```
This runs the search API on port `5000` (i.e. `http://<your-server-domain>:5000`) and automatically starts indexing the codebase.

### Step 2: Register the Custom Tool in GitHub
To register this tool for your organization:
1.  Go to your **GitHub Organization Settings** -> **Copilot** -> **Custom Tools** (or **Custom Skills**).
2.  Click **Add Custom Tool** (or **Add Skill**).
3.  Enter the URL to the OpenAPI definition served by your API:
    `http://<your-server-domain>:5000/swagger/v1/swagger.json`
4.  *Optional*: Set up authentication (e.g., API Keys or OAuth) if you restrict access to the server.
5.  Click **Save**.

### Step 3: Use it in VS Code
Any developer in your organization can now open VS Code, open the **GitHub Copilot Chat** panel, and invoke your custom search tool:
*   *"Search the codebase for reflection builder methods"*
*   *"Find where the DB context is configured"*

Copilot will automatically see the `/api/distill` endpoint in the OpenAPI contract, generate a JSON request payload, query your Docker container, receive the distilled code chunks, and use them to construct an accurate response—all without using local MCP extensions.
