/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/

'use strict';

import * as vscode from 'vscode';
import * as nls from 'vscode-nls';

const localize = nls.config(process.env.VSCODE_NLS_CONFIG)();


export function activate(context: vscode.ExtensionContext) {
	context.subscriptions.push(vscode.commands.registerCommand('extension.mono-debug.configureExceptions', () => configureExceptions()));
}

export function deactivate() {
}

function configureExceptions() {

	let options : vscode.QuickPickOptions = {
		placeHolder: localize('select.exception.category', "Select Exception Category"),
		matchOnDescription: true,
		matchOnDetail: true
	};

	vscode.window.showQuickPick([ "Category 1", "Category 2", "Category 3" ], options).then(item => {
		if (item) {
			vscode.window.showInformationMessage(`Selected: ${item}`);
		}
	});
}
