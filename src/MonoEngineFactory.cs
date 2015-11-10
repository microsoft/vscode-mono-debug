/*---------------------------------------------------------
 * Copyright (C) Microsoft Corporation. All rights reserved.
 *--------------------------------------------------------*/
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
