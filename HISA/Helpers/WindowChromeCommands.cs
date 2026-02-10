using System;
using System.Windows;
using System.Windows.Input;

namespace HISA
{
    public static class WindowChromeCommands
    {
        public static ICommand Minimize { get; } = new WindowChromeCommand(window => SystemCommands.MinimizeWindow(window));

        public static ICommand Maximize { get; } = new WindowChromeCommand(window => SystemCommands.MaximizeWindow(window));

        public static ICommand Restore { get; } = new WindowChromeCommand(window => SystemCommands.RestoreWindow(window));

        public static ICommand Close { get; } = new WindowChromeCommand(window => SystemCommands.CloseWindow(window));

        private sealed class WindowChromeCommand : ICommand
        {
            private readonly Action<Window> _execute;

            public WindowChromeCommand(Action<Window> execute)
            {
                _execute = execute;
            }

            public bool CanExecute(object parameter)
            {
                return parameter is Window;
            }

            public void Execute(object parameter)
            {
                if(parameter is Window window)
                {
                    _execute(window);
                }
            }

            public event EventHandler CanExecuteChanged
            {
                add { }
                remove { }
            }
        }
    }
}
