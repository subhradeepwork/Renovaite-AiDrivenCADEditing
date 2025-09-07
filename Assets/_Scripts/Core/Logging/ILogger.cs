namespace RenovAite.Core.Logging
{
    public interface ILogger
    {
        void Info(string category, string message);
        void Warn(string category, string message);
        void Error(string category, string message);
    }
}
