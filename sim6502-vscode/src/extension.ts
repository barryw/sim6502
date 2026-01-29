import * as path from 'path';
import * as vscode from 'vscode';
import {
    LanguageClient,
    LanguageClientOptions,
    ServerOptions,
    TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: vscode.ExtensionContext) {
    const config = vscode.workspace.getConfiguration('sim6502');
    let serverPath = config.get<string>('lspPath');

    if (!serverPath) {
        serverPath = 'dotnet';
    }

    const serverOptions: ServerOptions = {
        run: {
            command: serverPath,
            args: serverPath === 'dotnet'
                ? ['run', '--project', findLspProject(context)]
                : [],
            transport: TransportKind.stdio
        },
        debug: {
            command: serverPath,
            args: serverPath === 'dotnet'
                ? ['run', '--project', findLspProject(context)]
                : [],
            transport: TransportKind.stdio
        }
    };

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

function findLspProject(context: vscode.ExtensionContext): string {
    const workspaceFolder = vscode.workspace.workspaceFolders?.[0];
    if (workspaceFolder) {
        return path.join(workspaceFolder.uri.fsPath, 'sim6502-lsp', 'sim6502-lsp.csproj');
    }
    return 'sim6502-lsp/sim6502-lsp.csproj';
}

export function deactivate(): Thenable<void> | undefined {
    if (!client) {
        return undefined;
    }
    return client.stop();
}
