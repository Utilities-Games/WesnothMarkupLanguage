using System;

namespace WesnothMarkupLanguage
{
    /// <summary>
    /// Base class for all the errors encountered by the engine. It provides a field for storing custom messages related to the actual error.
    /// </summary>
    public class Error : Exception
    {

        public Error() : base()
        {

        }

        public Error(string message) : base(message)
        {

        }
    }
}
