using Google.Apis.Auth;
using Microsoft.AspNetCore.Mvc;

namespace Server.Controllers;

public sealed record GoogleLoginRequest(string IdToken);
public sealed record GoogleUserResponse(string Subject, string Email, string Name, string Picture);

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string GoogleClientId = "413729404676-dp2369quefq6tv75abuiu28nlvk87253.apps.googleusercontent.com";

    [HttpPost("google")]
    public async Task<ActionResult<GoogleUserResponse>> Google(GoogleLoginRequest request)
    {
        try
        {
            var payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken,
                new GoogleJsonWebSignature.ValidationSettings { Audience = new[] { GoogleClientId } });

            return Ok(new GoogleUserResponse(payload.Subject, payload.Email, payload.Name, payload.Picture ?? string.Empty));
        }
        catch
        {
            return Unauthorized(new { error = "Google token is invalid" });
        }
    }
}
