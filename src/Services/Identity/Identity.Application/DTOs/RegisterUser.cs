using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Zumaris.Identity.Application.DTOs;

public record RegisterRequest(
    string Username,
    string Password,
    string Email
);

public record RegisterResponse(
    bool Success,
    string Message
);

