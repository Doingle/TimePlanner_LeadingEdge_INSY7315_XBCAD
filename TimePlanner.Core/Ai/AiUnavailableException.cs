namespace TimePlanner.Core.Ai
{
    //-----------------------------
    //plain language error for ai failures
    public sealed class AiUnavailableException : Exception
    {
        public AiUnavailableException(string userMessage) : base(userMessage)
        {
        }

        public AiUnavailableException(string userMessage, Exception inner) : base(userMessage, inner)
        {
        }
    }
}
//------------------------------EOF-----------------------------\\
