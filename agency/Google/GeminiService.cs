using Google.GenAI;

namespace ShareInvest.Agency.Google;

/// <summary>
/// 
/// </summary>
public partial class GeminiService(string apiKey)
{
    /// <summary>
    /// 
    /// </summary>
    public Client Client => new(apiKey: apiKey);
}