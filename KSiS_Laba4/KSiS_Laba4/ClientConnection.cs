using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Windows;

namespace KSiS_Laba4
{
	public class ClientConnection
	{
		private const string RootDir = @"E:";
		private static string _curDir = @"\";
		private readonly TcpClient _controlClient;
		private readonly StreamReader _controlReader;

		private readonly StreamWriter _controlWriter;

		private BackgroundWorker _bw;

		private TcpClient _dataClient;

		private DataConnectionType _dataConnectionType;
		private DataEndpoint _dataEndpoint;
		private StreamWriter _dataWriter;
		private TcpListener _passiveListener;
		private string _transferType;
		private string _username;

		public ClientConnection(TcpClient client)
		{
			_controlClient = client;

			var controlStream = _controlClient.GetStream();

			_controlReader = new StreamReader(controlStream);
			_controlWriter = new StreamWriter(controlStream);
		}

		public void HandleClient(object obj, DoWorkEventArgs args)
		{
			_bw = (BackgroundWorker) obj;
			_controlWriter.WriteLine("220 Service Ready.");
			_bw.ReportProgress(0, "220 Service Ready.\r\n");
			_controlWriter.Flush();

			try
			{
				string line;
				while (!string.IsNullOrEmpty(line = _controlReader.ReadLine()))
				{
					_bw.ReportProgress(0, line + "\r\n");
					string response = null;

					var command = line.Split(' ');

					var cmd = command[0].ToUpperInvariant();
					var arguments = command.Length > 1 ? line.Substring(command[0].Length + 1) : null;

					if (string.IsNullOrWhiteSpace(arguments))
						arguments = null;

					switch (cmd)
					{
						case "USER":
							response = User(arguments);
							break;
						case "PASS":
							response = Password(arguments);
							break;
						case "LIST":
							response = List(arguments);
							break;
						case "RETR":
							response = Retr(arguments);
							break;
						case "STOR":
							response = Stor(arguments);
							break;
						case "ABOR":
							response = "502 Command not implemented";
							break;
						case "CWD":
							response = ChangeWorkingDirectory(arguments);
							break;
						case "CDUP":
							response = ChangeWorkingDirectoryUp();
							break;
						case "PWD":
							response = Pwd();
							break;
						case "QUIT":
							response = "221 Service closing control connection";
							break;
						case "TYPE":
							if (arguments != null)
							{
								var splitArgs = arguments.Split(' ');
								response = Type(splitArgs[0], splitArgs.Length > 1 ? splitArgs[1] : null);
							}
							break;
						case "PORT":
							response = Port(arguments);
							break;
						case "PASV":
							response = Passive();
							break;
						case "SIZE":
							response = Size(arguments);
							break;
						case "MDTM":
							response = Mdtm(arguments);
							break;
						case "STAT":
							response = Stat(arguments);
							break;
						case "NOOP":
							response = "200 OK";
							break;
						default:
							response = "502 Command not implemented";
							break;
					}


					if (_controlClient == null || !_controlClient.Connected)
					{
						break;
					}
					_bw.ReportProgress(0, response + "\r\n");
					_controlWriter.WriteLine(response);
					_controlWriter.Flush();

					if (response.StartsWith("221"))
					{
						break;
					}
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "ERROR!");
			}
		}

		private enum DataConnectionType
		{
			Active = 1,
			Passive = 0
		}

		public struct DataEndpoint
		{
			public IPAddress Address;
			public int Port;
		}

		#region FTP Commands

		private string Type(string typeCode, string formatControl)
		{
			string response;

			switch (typeCode)
			{
				case "A":
				case "I":
					_transferType = typeCode;
					response = "200 OK";
					break;
				case "E":
				case "L":
				default:
					response = "504 Command not implemented for that parameter.";
					break;
			}

			if (formatControl == null) return response;
			switch (formatControl)
			{
				case "N":
					response = "200 OK";
					break;
				case "T":
				case "C":
				default:
					response = "504 Command not implemented for that parameter.";
					break;
			}

			return response;
		}

		private string Port(string ipaddr)
		{
			var mas = ipaddr.Split(',');
			_dataEndpoint.Address = IPAddress.Parse($"{mas[0]}.{mas[1]}.{mas[2]}.{mas[3]}");
			var port = new[] {byte.Parse(mas[4]), byte.Parse(mas[5])};
			_dataEndpoint.Port = port[0]*256 + port[1];
			_dataConnectionType = DataConnectionType.Active;
			return "200 All OK";
		}

		private string Passive()
		{
			var rand = new Random();
			int port = rand.Next(10000) + 20000;
			_passiveListener = new TcpListener(new IPEndPoint(IPAddress.Any, port));
			_passiveListener.Start();
			return $"227 Entering Passive Mode (0,0,0,0,{port/256},{port%256}) - {IPAddress.Any}:{port}";
		}

		private string Stor(string filename)
		{
			var pathname = NormalizeFilename(filename);

			if (_dataConnectionType == DataConnectionType.Active)
			{
				_dataClient = new TcpClient();
				_dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoStore, pathname);
			}
			else
			{
				_passiveListener.BeginAcceptTcpClient(DoStore, pathname);
			}

			return $"150 Opening {_dataConnectionType} mode data transfer for STOR";
		}

		private void DoStore(IAsyncResult result)
		{
			if (_dataConnectionType == DataConnectionType.Active)
			{
				_dataClient.EndConnect(result);
			}
			else
			{
				_dataClient = _passiveListener.EndAcceptTcpClient(result);
			}

			var pathname = (string)result.AsyncState;

			using (var dataStream = _dataClient.GetStream())
			{
				using (var fs = new FileStream(pathname, FileMode.Create, FileAccess.Write))
				{
					CopyStream(dataStream, fs);
					_dataClient.Close();
					_dataClient = null;
					_controlWriter.WriteLine("226 Closing data connection, file transfer successful");
					_controlWriter.Flush();
				}
			}
		}

		private string Retr(string pathname)
		{
			pathname = NormalizeFilename(pathname);

			if (!File.Exists(pathname)) return "550 File Not Found";
			if (_dataConnectionType == DataConnectionType.Active)
			{
				_dataClient = new TcpClient();
				_dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoRetrieve, pathname);
			}
			else
			{
				_passiveListener.BeginAcceptTcpClient(DoRetrieve, pathname);
			}

			return $"150 Opening {_dataConnectionType} mode data transfer for RETR";
		}

		private static string NormalizeFilename(string pathname) => RootDir + _curDir + "/" + pathname;

		private void DoRetrieve(IAsyncResult result)
		{
			if (_dataConnectionType == DataConnectionType.Active)
			{
				_dataClient.EndConnect(result);
			}
			else
			{
				_dataClient = _passiveListener.EndAcceptTcpClient(result);
			}

			var pathname = (string) result.AsyncState;

			using (var dataStream = _dataClient.GetStream())
			{
				using (var fs = new FileStream(pathname, FileMode.Open, FileAccess.Read))
				{
					CopyStream(fs, dataStream);
					_dataClient.Close();
					_dataClient = null;
					_controlWriter.WriteLine("226 Closing data connection, file transfer successful");
					_controlWriter.Flush();
				}
			}
		}

		private static long CopyStream(Stream input, Stream output, int bufferSize)
		{
			var buffer = new byte[bufferSize];
			int count;
			long total = 0;

			while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
			{
				output.Write(buffer, 0, count);
				total += count;
			}

			return total;
		}

		private static long CopyStreamAscii(Stream input, Stream output, int bufferSize)
		{
			var buffer = new char[bufferSize];
			long total = 0;

			using (var rdr = new StreamReader(input))
			{
				using (var wtr = new StreamWriter(output, Encoding.ASCII))
				{
					int count;
					while ((count = rdr.Read(buffer, 0, buffer.Length)) > 0)
					{
						wtr.Write(buffer, 0, count);
						total += count;
					}
				}
			}

			return total;
		}

		private long CopyStream(Stream input, Stream output)
		{
			return _transferType == "I" ? CopyStream(input, output, 4096) : CopyStreamAscii(input, output, 4096);
		}

		private string Stat(string fileName)
		{
			var type = _transferType == "A" ? "ASCII" : "Binary";
			return
				"211-Maxi FTP server status:\r\n\r\n" +
				"Version 1.00E-INF\r\n\r\n" +
				$"Connected to {_dataEndpoint.Address}\r\n\r\n" +
				$"Logged in {_username}\r\n" +
				$"TYPE: {type}, FORM: Nonprint; STRUcture: File; transfer MODE: Stream\r\n" +
				"No data connection\r\n\r\n" +
				"211 End of status";
		}

		private static string Mdtm(string fileName) =>
			new FileInfo(RootDir + _curDir + '\\' + fileName).Exists
				? "213 " + new FileInfo(RootDir + _curDir + '\\' + fileName).LastWriteTimeUtc.ToUniversalTime()
					.Subtract(new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds
				: "550 File not found";

		private static string Size(string fileName) =>
			new FileInfo(RootDir + _curDir + '\\' + fileName).Exists
				? $"213 {new FileInfo(RootDir + _curDir + '\\' + fileName).Length}"
				: "550 File Not Found";

		private string User(string username)
		{
			_username = username;
			return $"331 Username {_username} ok, need password";
		}

		private string Password(string password)
		{
			return $"230 User {_username} logged in";
		}

		private static string ChangeWorkingDirectory(string pathname)
		{
			var dir = new DirectoryInfo(RootDir + pathname);
			if (!dir.Exists) return "553 Directory not found";
			try
			{
				var tryit = dir.GetFiles();
			}
			catch (Exception)
			{
				return "553 Directory not found";
			}
			_curDir = pathname;
			return "250 Changed to new directory";
		}

		private static string ChangeWorkingDirectoryUp()
		{
			if (_curDir == @"\") return "553 You in the ROOT directory";
			var dir = new DirectoryInfo(RootDir + "\\" + _curDir);
			_curDir = dir.Parent.Exists ? dir.Parent.FullName : _curDir;
			return "250 Changed to new directory";
		}

		private static string Pwd() => $"257 \"{_curDir}\" is current directory.";

		private string List(string pathname)
		{
			if (pathname == null)
			{
				pathname = string.Empty;
			}

			pathname = RootDir + _curDir;

			if (!(new DirectoryInfo(pathname)).Exists) return "450 Requested file action not taken";
			if (_dataConnectionType == DataConnectionType.Active)
			{
				_dataClient = new TcpClient();
				_dataClient.BeginConnect(_dataEndpoint.Address, _dataEndpoint.Port, DoList, pathname);
			}
			else
			{
				_passiveListener.BeginAcceptTcpClient(DoList, pathname);
			}

			return $"150 Opening {_dataConnectionType} mode data transfer for LIST";
		}

		private void DoList(IAsyncResult result)
		{
			if (_dataConnectionType == DataConnectionType.Active)
			{
				_dataClient.EndConnect(result);
			}
			else
			{
				_dataClient = _passiveListener.EndAcceptTcpClient(result);
			}

			var pathname = (string) result.AsyncState;
			using (var dataStream = _dataClient.GetStream())
			{
				_dataWriter = new StreamWriter(dataStream, Encoding.UTF8);

				var files = Directory.EnumerateFiles(pathname);
				foreach (var line in from file in files
					select new FileInfo(file)
					into f
					let date = f.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180)
						? f.LastWriteTime.ToString("MMM dd  yyyy")
						: f.LastWriteTime.ToString("MMM dd HH:mm")
					select ((f.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
						? $"-rw-r--r--    2 2003     2003     {f.Length,8} {date} {f.Name}"
						: "")
				{
					_dataWriter.WriteLine(line);
					_dataWriter.Flush();
				}

				var directories = Directory.EnumerateDirectories(pathname);
				foreach (var line in from dir in directories
					select new DirectoryInfo(dir)
					into d
					let date = d.LastWriteTime < DateTime.Now - TimeSpan.FromDays(180)
						? d.LastWriteTime.ToString("MMM dd  yyyy")
						: d.LastWriteTime.ToString("MMM dd HH:mm")
					select ((d.Attributes & FileAttributes.Hidden) != FileAttributes.Hidden)
						? $"drwxr-xr-x    2 2003     2003     {"4096",8} {date} {d.Name}"
						: "")
				{
					_dataWriter.WriteLine(line);
					_dataWriter.Flush();
				}
				_dataClient.Close();
				_dataClient = null;

				_controlWriter.WriteLine("226 Transfer complete");
				_bw.ReportProgress(0, "226 Transfer complete\r\n");
				_controlWriter.Flush();
			}
		}

		#endregion
	}
}