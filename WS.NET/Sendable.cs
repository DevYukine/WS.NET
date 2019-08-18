using System;
using System.Net.WebSockets;

namespace WS.NET
{
	/// <summary>
	/// Holds Data which should be send over WebSocket
	/// </summary>
	public class Sendable
	{
		/// <summary>
		/// Event called on Success
		/// </summary>
		public event EventHandler Success;

		/// <summary>
		/// Event called on Error
		/// </summary>
		public event EventHandler<Exception> Error;

		/// <summary>
		/// the data to send
		/// </summary>
		public byte[] Data { get; }
		
		/// <summary>
		/// The type of the message
		/// </summary>
		public WebSocketMessageType Type { get; }

		/// <summary>
		/// Creates a new instance of Sendable
		/// </summary>
		/// <param name="data">The data to send</param>
		/// <param name="type">The type of the message</param>
		public Sendable(byte[] data, WebSocketMessageType type)
		{
			Data = data;
			Type = type;
		}

		/// <summary>
		/// Emits either Success or Error event
		/// </summary>
		/// <param name="exception">optional, the exception encountered</param>
		public void Emit(Exception exception = null)
		{
			if (exception == null) Success?.Invoke(this, null);
			else Error?.Invoke(this, exception);
		}
	}
}