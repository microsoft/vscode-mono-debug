import * as vscode from 'vscode';
import { timeStamp } from 'console';

class XamarinEmulatorProvider implements vscode.TreeDataProvider<EmulatorItem> {
    constructor(private workspaceRoot: string) {}

    onDidChangeTreeData?: vscode.Event<void | EmulatorItem>;

    getTreeItem(element: EmulatorItem): vscode.TreeItem | Thenable<vscode.TreeItem> {
        return element;
    }

    getChildren(element?: EmulatorItem): vscode.ProviderResult<EmulatorItem[]> {

        vscode.window.showInformationMessage("Starting getChildren()!");

        // if (!this.workspaceRoot) {
        //     vscode.window.showInformationMessage("Not in workspace root!")
        //     return 
        // }

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

    constructor(public readonly name: string, public readonly collapsibleState: vscode.TreeItemCollapsibleState = vscode.TreeItemCollapsibleState.None) 
    {
        super(name, collapsibleState);
    }

    get tooltip(): string {
		return `${this.label}-Android emulator`;
    }
    
    // description()
    // contextValue = 

}

 export default XamarinEmulatorProvider;