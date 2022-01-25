using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive;
using System.Text;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using ReactiveUI;

namespace HeadlessFS.ViewModels
{
    public class Server
    {
        private TcpListener _listener = new TcpListener(IPAddress.Parse("127.0.0.1"), 52000);
        private TcpClient _client;
        private bool _keepAlive = true;
        
        public void Start()
        {
            Console.WriteLine("STARTED");
            _listener.Start();
            _client = new TcpClient();
            var th = new Thread(Loop);
            _keepAlive = true;
            th.Start();
        }

        public void Stop()
        {
            _keepAlive = false;
            _listener.Stop();
        }

        private void Loop()
        {
            while (_keepAlive)
            {
                _client = _listener.AcceptTcpClient();
                var isConnected = true;
                Console.WriteLine("Connected!");
                NetworkStream stream = _client.GetStream();
                stream.ReadTimeout = 1;
                while (isConnected)
                {
                    if (_keepAlive == false) break;
                    try
                    {
                        
                        var result = stream.ReadByte();
                        if (result == -1)
                        {
                            stream.WriteByte(0x00);
                            continue;
                        }
                        AppendByte((byte)result);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        isConnected = false;
                    }
                }
            }
        }

        
        
        private void AppendByte(byte value)
        {
            Console.WriteLine("REC " + $"{value:X2}");
        }
    }
    
    
    public class MainWindowViewModel : ViewModelBase
    {
        private string ImagePath { get; set; } = string.Empty;

        
        private int _currentBlock;
        private bool _updateLock;
        
        public int CurrentBlock
        { 
            get => _currentBlock;
            set
            {
                if (value == _currentBlock) return;
                this.RaiseAndSetIfChanged(ref _currentBlock, value);
                _updateLock = true;
                CurrentPage = 0;
                _updateLock = false;
                Seek(value, 0);
            }
        }
        
        private int _currentPage;
        public int CurrentPage
        { 
            get => _currentPage;
            set 
            {                 
                if (value == _currentPage) return;
                this.RaiseAndSetIfChanged(ref _currentPage, value);
                if (_updateLock) return;
                Seek(CurrentBlock, value);
            }
        }

        public ObservableCollection<string> CurrentData { get; } = new();

        private void Seek(int block, int page)
        {
            var buff = new byte[4224];
            using (var fs = File.Open(ImagePath, FileMode.Open))
            {
                Console.WriteLine($"SEEKING TO {block} {page} in file {ImagePath}");
                fs.Seek(4224 * 64 * block + 4224 * page, SeekOrigin.Begin);
                fs.Read(buff, 0, 4224);
                fs.Close();
            }
            CurrentData.Clear();
            for (int i = 0; i < 4224/64; i++)
            {
                var currentArray = buff.Skip(i * 64).Take(64).ToArray();
                CurrentData.Add(ByteArrayToString(currentArray) );
            }
        }

        private static string ByteArrayToString(IReadOnlyCollection<byte> ba)
        {
            StringBuilder hex = new(ba.Count * 2);
            foreach (var b in ba)
            {
                hex.AppendFormat("{0:X2} ", b);
            }
            return hex.ToString().TrimEnd();
        }
 

        

 
        
        public ReactiveCommand<Unit,Unit> FormatCommand { get; }
        private void FormatAction()
        {
            var buff = new byte[4224];

            for (var j = 0; j < 4224; j++)
            {
                buff[j] = 0xFF;
            }
            
            using var fs = File.Open(ImagePath, FileMode.Open);
            for (var i = 0; i < 4096 * 64; i++)
            {
                fs.Write(buff, 0, 4224);
                fs.Flush();
            }
        }
 

        public ReactiveCommand<Unit,Unit> LoadImageCommand { get; }
        private async void LoadImageAction()
        {
            if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime) return;
            var result = await new OpenFileDialog().ShowAsync(desktopLifetime.MainWindow);
            if (result == null) return;
            if (string.IsNullOrEmpty(result[0])) return;
            ImagePath = result[0];
            SaveLastImageLocation(ImagePath);
            Seek(0,0);
        }
        
        public ReactiveCommand<Unit,Unit> CreateImageCommand { get; }
        private async void CreateImageAction()
        {
            if (Application.Current.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktopLifetime) return;
            var result = await new SaveFileDialog().ShowAsync(desktopLifetime.MainWindow);
            if (string.IsNullOrEmpty(result)) return;
            SaveLastImageLocation(result);
            ImagePath = result;
            File.Create(ImagePath).Close();
            FormatAction();
            Seek(0,0);
        }
        
        public ReactiveCommand<Unit,Unit> RefreshCommand { get; }
        private void RefreshAction()
        {
            Seek(CurrentBlock, CurrentPage);
        }

        private Server _server = new();
        
        public ReactiveCommand<Unit,Unit> ToggleServerCommand { get; }
        private void ToggleServerAction()
        {
            _server.Stop();
            _server.Start();
        }
 

        public MainWindowViewModel()
        {
            if (File.Exists("lastLocation.txt"))
            {
                ImagePath = File.ReadAllText("lastLocation.txt");
                if (!File.Exists(ImagePath))
                {
                    File.Create(ImagePath);
                    FormatAction();
                }
                Seek(0, 0);
            }

            
            LoadImageCommand = ReactiveCommand.Create(LoadImageAction);
            CreateImageCommand = ReactiveCommand.Create(CreateImageAction);
            FormatCommand = ReactiveCommand.Create(FormatAction);
            RefreshCommand = ReactiveCommand.Create(RefreshAction);
            ToggleServerCommand = ReactiveCommand.Create(ToggleServerAction);
        }


        private static void SaveLastImageLocation(string newPath)
        {
            // CHECK IF FILE EXISTS
            if (!File.Exists("lastLocation.txt"))
            {
                File.Create("lastLocation.txt").Close();
            }

            using (var fs = File.Open("lastLocation.txt", FileMode.Open))
            {
                fs.Write(Encoding.ASCII.GetBytes(newPath));
                fs.Flush();
                fs.Close();
            }
        }
    }
}