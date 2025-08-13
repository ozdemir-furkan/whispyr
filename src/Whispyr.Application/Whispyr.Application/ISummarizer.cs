namespace Whispyr.Application.Interfaces
{
    public interface ISummarizer
    {
        Task<string> SummarizeAsync(string text);
    }
}