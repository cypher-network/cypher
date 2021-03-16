using Terminal.Gui;

namespace CYPCore.Terminal
{
    public class StatusBar : FrameView
    {
        private readonly Label _label;

        public StatusBar(Pos frameY)
        {
            X = 0;
            Y = frameY;
            Width = Dim.Fill();
            Height = 3;

            _label = new Label
            {
                X = X + 1,
                Y = 0,
                Width = Dim.Fill() - 1,
                Height = 1
            };

            Add(_label);
        }

        public new string Text
        {
            set
            {
                Application.MainLoop.Invoke(() => _label.Text = value);
            }
        }
    }
}