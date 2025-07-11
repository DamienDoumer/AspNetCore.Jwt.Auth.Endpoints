﻿using Microsoft.Extensions.Options;
using The.Jwt.Auth.Endpoints.Endpoints.Requests;
using The.Jwt.Auth.Endpoints.Endpoints.Responses;
using The.Jwt.Auth.Endpoints.Extensions;
using The.Jwt.Auth.Endpoints.Helpers;
using The.Jwt.Auth.Endpoints.Helpers.Exceptions;
using The.Jwt.Auth.Endpoints.Settings;
using The.Jwt.Auth.Endpoints.UseCases;

namespace The.Jwt.Auth.Endpoints.Endpoints;

internal static class GoogleAuthEndpoint
{
    public const string Name = "GoogleSocialAuthentication";

    public static IEndpointRouteBuilder MapGoogleAuthenticationEndpoint<TUser>(this IEndpointRouteBuilder app)
        where TUser : IdentityUser
    {
        app.MapPost(AuthConstants.GoogleEndpoint, 
                async ([FromBody] GoogleAuthRequestModel googleAuthRequest, 
                       [FromServices] IIdentityUserFactory<TUser> userFactory,
                       [FromServices] IOptions<JwtAuthEndpointsConfigOptions> configOptions,
                       [FromServices] UserManager<TUser> userManager,
                       [FromServices] IJwtTokenProvider jwtProvider) =>
                {
                    var validationResult = googleAuthRequest.ValidateModel();
                    if (validationResult != null)
                    {
                        return validationResult.CreateValidationErrorResult();
                    }

                    try
                    {
                        var firebaseToken = await FirebaseAdmin.Auth.FirebaseAuth.DefaultInstance
                            .VerifyIdTokenAsync(googleAuthRequest.Token);

                        var picture = firebaseToken.Claims[JwtRegisteredClaimNames.Picture]?.ToString();
                        var email = firebaseToken.Claims[JwtRegisteredClaimNames.Email].ToString();

                        var user = await userManager.FindByEmailAsync(email!);
                        AuthToken? token = null;

                        if (user != null)
                        {
                            token = await jwtProvider.CreateToken(user.Id);
                            return Results.Ok(AuthResponseModel.FromAuthToken(token));
                        }

                        var displayName = firebaseToken.Claims[JwtRegisteredClaimNames.Name].ToString()!;
                        var names = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var result = await userManager.Register(userFactory,
                                names.First(), names.Last(), email!, isSocialAuth: true);

                        token = await jwtProvider.CreateToken(result.Id);
                        return Results.Ok(AuthResponseModel.FromAuthToken(token));
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
                        if (e.Source == "FirebaseAdmin")
                        {
                            return Results.Problem(new ProblemDetails
                            {
                                Title = e.Message,
                                Status = StatusCodes.Status400BadRequest
                            });
                        }

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
