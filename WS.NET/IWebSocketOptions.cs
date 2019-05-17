using System;
using System.Collections.Generic;
using System.Net;

namespace WS.NET
{
	/// <summary>
	/// Options for the WebSocketClient.
	/// </summary>
	public interface IWebSocketOptions
	{
		/// <summary>
		/// The headers which should be send when connecting.
		/// </summary>
		IEnumerable<Tuple<string, string>> Headers { get; set; }
		
		/// <summary>
		/// A proxy instance to use if required.
		/// </summary>
		IWebProxy Proxy { get; set; }
	}
}