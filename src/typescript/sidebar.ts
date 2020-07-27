import * as vscode from 'vscode';

class XamarinEmulatorProvider implements vscode.TreeDataProvider<EmulatorItem> {
    constructor(private workspaceRoot: string) {}

    onDidChangeTreeData?: vscode.Event<void | EmulatorItem>;

    getTreeItem(element: EmulatorItem): vscode.TreeItem | Thenable<vscode.TreeItem> {
        return element;
    }

    getChildren(element?: EmulatorItem): vscode.ProviderResult<EmulatorItem[]> {
        return Promise.resolve(this.getDummyData());
    }

    getParent?(element: EmulatorItem): vscode.ProviderResult<EmulatorItem> {
        throw new Error("Method not implemented.");
    }

    getDummyData() : EmulatorItem[] {
        return [
            new EmulatorItem("first"),
            new EmulatorItem("second"),
            new EmulatorItem("third")
        ]
    }
    
}

class EmulatorItem extends vscode.TreeItem {

    constructor(public readonly name: string, public readonly collapsibleState?: vscode.TreeItemCollapsibleState)
    {
        super(name, collapsibleState);
    }

    // tooltip()
    // description()
}

 export default XamarinEmulatorProvider;