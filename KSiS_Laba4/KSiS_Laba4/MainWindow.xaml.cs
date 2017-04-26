using System.Windows;

namespace KSiS_Laba4
{
	public partial class MainWindow
	{
		private readonly FtpServer _server;

		public MainWindow()
		{
			InitializeComponent();
			_server = new FtpServer(TbMain);
			TbMain.Text = "";
			BStart_Click(this, null);
		}

		private void BStart_Click(object sender, RoutedEventArgs e)
		{
			BStart.Visibility = Visibility.Collapsed;
			BStop.Visibility = Visibility.Visible;
			_server.Start();
		}

		private void BStop_Click(object sender, RoutedEventArgs e)
		{
			BStart.Visibility = Visibility.Visible;
			BStop.Visibility = Visibility.Collapsed;
			_server.Stop();
		}
	}
}