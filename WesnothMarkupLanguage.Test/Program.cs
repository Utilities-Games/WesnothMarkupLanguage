using System;
using WesnothMarkupLanguage.Test.Views;

namespace WesnothMarkupLanguage.Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var mainView = new MainView();
            mainView.Run();
        }
    }
}
