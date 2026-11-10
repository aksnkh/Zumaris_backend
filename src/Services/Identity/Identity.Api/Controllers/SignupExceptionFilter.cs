using Identity.Api.Security;
using Identity.Application.Features.Signup;
using Identity.Application.Features.Auth.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Zumaris.BuildingBlocks.Common.Responses;
namespace Identity.Api.Controllers;
public sealed class SignupExceptionFilter(ILogger<SignupExceptionFilter> logger):IExceptionFilter
{
 public void OnException(ExceptionContext context)
 {
  if(context.Exception is OperationCanceledException&&context.HttpContext.RequestAborted.IsCancellationRequested)return;
  var ex=context.Exception as SignupException;var status=ex?.Status??500;
  if(ex is null)logger.LogError(context.Exception,"Account signup failed.");
  context.Result=new ObjectResult(ApiResponseError.Create(status,ex?.Message??"Account creation is temporarily unavailable.",context.HttpContext.TraceIdentifier,ex?.Errors)){StatusCode=status};context.ExceptionHandled=true;
 }
}
