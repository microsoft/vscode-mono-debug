/*---------------------------------------------------------------------------------------------
 *  Copyright (c) Microsoft Corporation. All rights reserved.
 *  Licensed under the MIT License. See License.txt in the project root for license information.
 *--------------------------------------------------------------------------------------------*/
using System;

namespace OpenDebug
{
	public class EngineFactory
	{
		public static IDebugSession CreateDebugSession(string adapterID, Action<DebugEvent> callback)
		{

			OperatingSystem os = Environment.OSVersion;
			PlatformID pid = os.Platform;

			if ((pid == PlatformID.MacOSX || pid == PlatformID.Unix) && adapterID == "mono") {
				return new SDBDebugSession(callback);
			}

			return null;
		}
	}
}
