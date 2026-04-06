using OpenAI;

using System.ClientModel;

namespace ShareInvest.Agency.OpenAI;

/// <summary>
/// 
/// </summary>
public partial class OpenCodeService : OpenAIClient
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="apiKey"></param>
    public OpenCodeService(string apiKey) : base(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri("") })
    {

    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="apiKey"></param>
    /// <param name="imageModel"></param>
    public OpenCodeService(string apiKey, string imageModel) : base(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = new Uri("") })
    {
        this.imageModel = imageModel;
    }

    readonly string? imageModel;
}