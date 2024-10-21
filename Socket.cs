using System;
using SocketIOClient;


namespace ArcademiaGameLauncher
{
    internal class Socket
    {
        readonly MainWindow mainWindow;
        readonly SocketIOClient.SocketIO client;
        private readonly string clientName;

        public Socket(string ip, string port, string clientName, MainWindow mainWindow)
        {
            Console.WriteLine("[Socket] Connecting to " + ip + ":" + port);

            this.mainWindow = mainWindow;
            client = new SocketIOClient.SocketIO("ws://" + ip + ":" + port, new SocketIOOptions()
            {
                Reconnection = true,
                ReconnectionDelay = 3000,
                ReconnectionDelayMax = 5000,
                ConnectionTimeout = TimeSpan.FromSeconds(5)
            });
            this.clientName = clientName;

            HandleEmits();
            Connect();
        }

        private async void Connect() => await client.ConnectAsync();

        private void HandleEmits()
        {
            client.OnConnected += Socket_OnConnected;
            client.OnDisconnected += Socket_OnDisconnected;
            client.OnError += Socket_OnError;
            client.OnReconnected += Socket_OnReconnected;
            client.OnReconnectAttempt += Socket_OnReconnecting;
            client.OnReconnectFailed += Socket_OnReconnectFailed;

            client.On("fetchAudio", response => Socket_FetchAudio(response));
            client.On("playAudio", response => Socket_PlayAudio(response));
        }

        private void Socket_OnConnected(object sender, EventArgs e)
        {
            Console.WriteLine("[Socket] Connected");

            client.EmitAsync("register", clientName);
        }

        private void Socket_OnDisconnected(object sender, string e)
        {
            Console.WriteLine("[Socket] Disconnected");
        }

        private void Socket_OnError(object sender, string e)
        {
            Console.WriteLine("[Socket] Error: " + e);
        }

        private void Socket_OnReconnected(object sender, int e)
        {
            Console.WriteLine("[Socket] Reconnected");
        }

        private void Socket_OnReconnecting(object sender, int e)
        {
            Console.WriteLine("[Socket] Reconnecting");
        }

        private void Socket_OnReconnectFailed(object sender, EventArgs e)
        {
            Console.WriteLine("[Socket] Reconnect failed");
        }

        private void Socket_FetchAudio(SocketIOResponse response)
        {
            Console.WriteLine("[Socket] Fetching audio files");

            mainWindow.DownloadAudioFiles();

            client.EmitAsync("fetchedAudio", string.Join(",", mainWindow.GetAudioFileNames()));
        }

        private void Socket_PlayAudio(SocketIOResponse response)
        {
            if (response.GetValue<int>() >= mainWindow.GetAudioFileNames().Length)
            {
                Console.WriteLine("[Socket] Audio file index out of range");
                return;
            }

            Console.WriteLine("[Socket] Playing audio file " + mainWindow.GetAudioFileNames()[response.GetValue<int>()] + ".wav");

            mainWindow.PlayAudioFile(mainWindow.GetAudioFileNames()[response.GetValue<int>()]);
        }
    }
}
