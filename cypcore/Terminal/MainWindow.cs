using System.Threading;
using CYPCore.Models;
using Serilog;
using Terminal.Gui;

namespace CYPCore.Terminal
{
    public class MainWindow
    {
        private readonly Toplevel _top;
        private readonly Window _window;

        private readonly FrameView _sectionFrame;
        private readonly FrameView _contentFrame;
        private readonly StatusBar _statusBar;

        private readonly CancellationTokenSource _cancellationTokenSource;

        public MainWindow(CancellationTokenSource cancellationTokenSource)
        {
            _cancellationTokenSource = cancellationTokenSource;

            Application.Init();
            _top = Application.Top;

            _window = new Window()
            {
                X = 0,
                Y = 1,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            _top.Add(_window);

            var menu = new MenuBar(
                new[]
                {
                    new MenuBarItem("_File", new MenuItem[]
                    {
                        new ("_Quit", "", () =>
                        {
                            _cancellationTokenSource.Cancel();
                            Application.RequestStop();
                            
                            // TODO: Find a better way to restore the console on application exit than to enforce an error
                            Application.Shutdown();
                        })
                    })
                });

            _top.Add(menu);

            _sectionFrame = new FrameView()
            {
                X = 0,
                Y = Pos.Bottom(menu),
                Height = Dim.Fill() - 3,
                Width = 25
            };
            _top.Add(_sectionFrame);

            _contentFrame = new FrameView()
            {
                X = Pos.Right(_sectionFrame),
                Y = _sectionFrame.Y,
                Height = _sectionFrame.Height,
                Width = _window.Width
            };
            _top.Add(_contentFrame);

            _statusBar = new StatusBar(Pos.Bottom(_sectionFrame));
            _top.Add(_statusBar);
        }

        public void Start()
        {
            Application.Run();
        }

        public StatusBar StatusBar => _statusBar;
    }
}