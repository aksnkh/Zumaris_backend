using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Zumaris.BuildingBlocks.Common.Responses;

public class Response<T>
{
    public int StatusCode { get; set; }
    public string Message { get; set; }
    public string Title { get; set; }
    public MessageLevel MessageLevel { get; set; }
    public string InformationLink { get; set; }
    public string DevMessage { get; set; }
    public string Parameters { get; set; }
    public T Payload { get; set; }

    public Response()
    {
    }

    public Response(T payload)
    {
        Payload = payload;
    }
}

