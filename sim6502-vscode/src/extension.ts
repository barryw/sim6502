import * as path from 'path';
import * as fs from 'fs';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;
let outputChannel: vscode.OutputChannel;

export function activate(context: vscode.ExtensionContext) {
    outputChannel = vscode.window.createOutputChannel('sim6502 Language Server');
    outputChannel.appendLine('sim6502 extension activating...');

    const config = vscode.workspace.getConfiguration('sim6502');
    const configuredPath = config.get<string>('lspPath');

    outputChannel.appendLine(`Configured lspPath: "${configuredPath || '(not set)'}"`);

    if (configuredPath) {
        outputChannel.appendLine(`Using configured path: ${configuredPath}`);
        startWithExecutable(configuredPath, context);
    } else {
        const projectPath = findLspProject();
        if (projectPath) {
            outputChannel.appendLine(`Found LSP project: ${projectPath}`);
            startWithDotnetRun(projectPath, context);
        } else {
            outputChannel.appendLine('ERROR: Could not find sim6502-lsp project');
            vscode.window.showErrorMessage(
                'sim6502 Language Server not found. Please set "sim6502.lspPath" in settings to the path of the sim6502-lsp executable or project.'
            );
            return;
        }
    }
}

function startWithExecutable(execPath: string, context: vscode.ExtensionContext) {
    const serverOptions: ServerOptions = {
        run: { command: execPath, args: [], transport: TransportKind.stdio },
        debug: { command: execPath, args: [], transport: TransportKind.stdio }
    };
    startClient(serverOptions, context);
}

function startWithDotnetRun(projectPath: string, context: vscode.ExtensionContext) {
    const serverOptions: ServerOptions = {
        run: { command: 'dotnet', args: ['run', '--project', projectPath], transport: TransportKind.stdio },
        debug: { command: 'dotnet', args: ['run', '--project', projectPath], transport: TransportKind.stdio }
    };
    startClient(serverOptions, context);
}

function startClient(serverOptions: ServerOptions, context: vscode.ExtensionContext) {
    const clientOptions: LanguageClientOptions = {
        documentSelector: [{ scheme: 'file', language: 'sim6502' }],
        synchronize: {
            fileEvents: vscode.workspace.createFileSystemWatcher('**/*.6502')
        }
    };

    client = new LanguageClient(
        'sim6502',
        'sim6502 Language Server',
        serverOptions,
        clientOptions
    );

    client.start();
}

function findLspProject(): string | undefined {
    outputChannel.appendLine(`Workspace folders: ${JSON.stringify(vscode.workspace.workspaceFolders?.map(f => f.uri.fsPath) ?? [])}`);

    const candidates = [
        // Check workspace folders
        ...( vscode.workspace.workspaceFolders?.map(f =>
            path.join(f.uri.fsPath, 'sim6502-lsp', 'sim6502-lsp.csproj')
        ) ?? []),
        // Check parent of workspace (for monorepo setups)
        ...( vscode.workspace.workspaceFolders?.map(f =>
            path.join(path.dirname(f.uri.fsPath), 'sim6502-lsp', 'sim6502-lsp.csproj')
        ) ?? []),
        // Check sibling folder named sim6502
        ...( vscode.workspace.workspaceFolders?.map(f =>
            path.join(path.dirname(f.uri.fsPath), 'sim6502', 'sim6502-lsp', 'sim6502-lsp.csproj')
        ) ?? [])
    ];

    outputChannel.appendLine(`Checking candidates:`);
    for (const candidate of candidates) {
        const exists = fs.existsSync(candidate);
        outputChannel.appendLine(`  ${candidate} - ${exists ? 'EXISTS' : 'not found'}`);
        if (exists) {
            return candidate;
        }
    }

    return undefined;
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
