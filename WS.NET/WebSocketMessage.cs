using System;
using System.Net.WebSockets;
using System.Threading.Tasks;

namespace WS.NET
{
	public class WebSocketMessage
	{
		/// <summary>
		/// Creates new WebSocketMessage from Data & Type
		/// </summary>
		/// <param name="data"></param>
		/// <param name="type"></param>
		public WebSocketMessage(byte[] data, WebSocketMessageType type)
		{
			Type = type;
			Data = data;
		}

		/// <summary>
		/// The Data of this WebSocketMessage
		/// </summary>
		public byte[] Data { get; }
		
		/// <summary>
		/// The Type of this WebSocketMessage
		/// </summary>
		public WebSocketMessageType Type { get; }

		/// <summary>
		/// The Task of this WebSocketMessage
		/// </summary>
		public Task Task
			=> TaskCompletionSource.Task;

		/// <summary>
		/// Finishes this Request
		/// </summary>
		/// <param name="exception">Optional exception which occured</param>
		public void Complete(Exception exception = null)
		{
			if (exception == null)
				TaskCompletionSource.SetResult(true);
			else
				TaskCompletionSource.SetException(exception);
		}

		private TaskCompletionSource<bool> TaskCompletionSource { get; } = new TaskCompletionSource<bool>();
	}
}
