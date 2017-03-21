using System;
using Newtonsoft.Json.Linq;

namespace ConsoleApp
{
    class Program
    {
        static void Main(string[] args)
        {
            JObject jObject = JObject.Parse(" { 'Success': true } ");

            Console.WriteLine($"Success: {jObject["Success"]}");
        }
    }
}
