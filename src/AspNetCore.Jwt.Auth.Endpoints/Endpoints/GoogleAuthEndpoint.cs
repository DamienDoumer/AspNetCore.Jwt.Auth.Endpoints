﻿using AspNetCore.Jwt.Auth.Endpoints.Endpoints.Requests;
using AspNetCore.Jwt.Auth.Endpoints.Endpoints.Responses;
using AspNetCore.Jwt.Auth.Endpoints.Extensions;
using AspNetCore.Jwt.Auth.Endpoints.Helpers;
using AspNetCore.Jwt.Auth.Endpoints.Helpers.Exceptions;
using AspNetCore.Jwt.Auth.Endpoints.Settings;
using AspNetCore.Jwt.Auth.Endpoints.UseCases;
using Microsoft.Extensions.Options;

namespace AspNetCore.Jwt.Auth.Endpoints.Endpoints;

internal static class GoogleAuthEndpoint
{
    public const string Name = "GoogleSocialAuthentication";

    public static IEndpointRouteBuilder MapGoogleAuthenticationEndpoint<TUser>(this IEndpointRouteBuilder app)
        where TUser : IdentityUser
    {
        app.MapPost(AuthConstants.GoogleEndpoint, 
                async ([FromBody] GoogleAuthRequestModel googleAuthRequest, [FromServices] IServiceProvider serviceProvider) =>
                {
                    try
                    {
                        var firebaseToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance
                            .VerifyIdTokenAsync(googleAuthRequest.Token);

                        var picture = firebaseToken.Claims[JwtRegisteredClaimNames.Picture]?.ToString();
                        var email = firebaseToken.Claims[JwtRegisteredClaimNames.Email]?.ToString();

                        var userFactory = serviceProvider.GetRequiredService<IIdentityUserFactory<TUser>>();
                        var configOptions = serviceProvider.GetRequiredService<IOptions<JwtAuthEndpointsConfigOptions>>();
                        var userManager = serviceProvider.GetRequiredService<UserManager<TUser>>();
                        var jwtProvider = serviceProvider.GetRequiredService<IJwtTokenProvider>();
                        var user = await userManager.FindByEmailAsync(email!);

                        if (user != null)
                        {
                            var token = await jwtProvider.CreateToken(user.Id);
                            return Results.Ok(new AuthResponseModel
                                {
                                    ExpiresAt = DateTimeOffset.Now.AddMinutes(token.JwtTokenLifeSpanInMinute),
                                    RefreshTokenExpiryInMinutes = token.RefreshTokenLifeSpanInMinutes,
                                    RefreshToken = token.RefreshToken,
                                    Token = token.JwtToken,
                                    TokenExpiryInMinutes = token.JwtTokenLifeSpanInMinute
                                });
                        }

                        var displayName = firebaseToken.Claims["name"].ToString()!;
                        var names = displayName.Split(' ');
                        var result = await userManager.Register(userFactory,
                                names.First(), names.Last(), email!, isSocialAuth: true);

                        return Results.Ok(result);
                    }
                    catch (BaseException e)
                    {
                        return Results.Problem(new ProblemDetails
                        {
                            Title = e.Message,
                            Status = e.StatusCode
                        });
                    }
                    catch (Exception e)
                    {
                        return Results.Problem(new ProblemDetails
                        {
                            Title = e.Message,
                            Status = StatusCodes.Status500InternalServerError
                        });
                    }
                })
        .WithName(Name)
        .AllowAnonymous()
        .Produces<AuthResponseModel>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
        .WithTags(AuthEndpointExtentions.Tag);

        return app;
    }
}
