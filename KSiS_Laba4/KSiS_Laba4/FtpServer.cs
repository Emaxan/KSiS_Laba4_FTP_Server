using System;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

namespace KSiS_Laba4
{
	public class FtpServer
	{
		private readonly TextBox _box;
		private TcpListener _listener;

		public FtpServer(TextBox box)
		{
			_box = box;
		}

		public void Start()
		{
			_listener = new TcpListener(IPAddress.Any, 21);
			_listener.Start();
			_listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
		}

		public void Stop()
		{
			_listener?.Stop();
		}

		private void HandleAcceptTcpClient(IAsyncResult result)
		{
			try
			{
				_listener.BeginAcceptTcpClient(HandleAcceptTcpClient, _listener);
				var client = _listener.EndAcceptTcpClient(result);

				var connection = new ClientConnection(client);

				var bw = new BackgroundWorker {WorkerReportsProgress = true};
				bw.DoWork += connection.HandleClient;
				bw.ProgressChanged += Bw_ProgressChanged;
				bw.RunWorkerAsync(client);
			}
			catch (Exception e)
			{
				MessageBox.Show(e.Message);
			}
		}

		private void Bw_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			_box.Text += e.UserState;
		}
	}
}