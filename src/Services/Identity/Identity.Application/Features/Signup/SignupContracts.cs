using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Identity.Application.Features.Auth;
using Identity.Application.Features.Auth.Models;
using Identity.Application.Abstractions.Security;
using System.Text.RegularExpressions;
using Zumaris.Identity.Domain.Entities;
namespace Identity.Application.Features.Signup;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SignupRequest(
 [Required,StringLength(100,MinimumLength=1)] string FirstName,
 [Required,StringLength(100,MinimumLength=1)] string LastName,
 [Required,StringLength(254)] string Email,
 [Required,StringLength(40,MinimumLength=1)] string Phone,
 [Required,StringLength(1024,MinimumLength=8)] string Password);
public sealed record SignupAccount(string FirstName,string LastName,string Email,string Phone,string PasswordHash);
public interface ISignupRepository
{
 Task<User> CreateAsync(SignupAccount account,CancellationToken ct);
 Task<User?> FindByEmailAsync(string email,CancellationToken ct);
}
public sealed class SignupException(int status,string message,Dictionary<string,string[]>? errors=null):Exception(message)
{public int Status{get;}=status;public Dictionary<string,string[]>? Errors{get;}=errors;}
public sealed class SignupService(ISignupRepository repository,IPasswordHasher passwords,IUserSessionIssuer sessions)
{
 public async Task<(AuthResponse Response,string RefreshToken)> CreateAsync(SignupRequest input,CancellationToken ct)
 {
  var account=SignupValidation.ValidateAndHash(input,passwords);
  var user=await repository.CreateAsync(account,ct);
  try{return await sessions.IssueAsync(user,ct);}
  catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
  catch{throw new SignupException(503,"Your account was created, but sign-in is temporarily unavailable. Sign in to continue.");}
 }
}

// Shared account rules; administrator creation does not weaken normal signup validation.
public static class SignupValidation
{
 public static SignupAccount ValidateAndHash(SignupRequest input,IPasswordHasher passwords)
 {
  var errors=new Dictionary<string,string[]>();
  string Name(string? value,string field)
  {var name=value?.Trim()??"";if(name.Length is <1 or >100||name.Any(char.IsControl))errors[field]=[$"{field} must contain 1 to 100 characters without control characters."];return name;}
  var first=Name(input.FirstName,"firstName");var last=Name(input.LastName,"lastName");
  if(first.Length+last.Length+1>200)errors["firstName"]=["First name and last name together must not exceed 199 characters."];
  var email=input.Email?.Trim().ToLowerInvariant()??"";
  if(email.Length is <3 or >254||!new EmailAddressAttribute().IsValid(email))errors["email"]=["Enter a valid email address of up to 254 characters."];
  var phone=input.Phone?.Trim()??"";
  if(phone.Length>40||!Regex.IsMatch(phone,@"\A\+[0-9 ()-]+\z"))errors["phone"]=["Enter an international phone number starting with +."];
  else{phone="+"+new string(phone.Where(char.IsAsciiDigit).ToArray());if(!Regex.IsMatch(phone,@"\A\+[1-9][0-9]{6,14}\z"))errors["phone"]=["The phone number must contain 7 to 15 digits and a valid country code."];}
  if(string.IsNullOrWhiteSpace(input.Password)||input.Password.Length is <8 or >1024)errors["password"]=["Password must contain 8 to 1024 characters."];
  if(errors.Count>0)throw new SignupException(400,string.Join(" ",errors.Values.SelectMany(x=>x)),errors);
  return new(first,last,email,phone,passwords.Hash(input.Password));
 }
}
