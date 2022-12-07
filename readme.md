# Beyond
The C# version of the Beyond bot, made to run off of .NET 6

## NuGet Dependencies
- AWS DynamoDBv2
- Discord.NET
- Microsoft.Extensions.DependencyInjection

## Getting Started
Create a .env file, and place it into the solution folder. The build task will copy this .env file to build - if sharing, take care to remove the .env file beforehand since it contains client secrets!

## Known Issues
Dynamo DB queries are not paginated, and work under the assumption that any given query will not be under 1	MB. This does not make it very scalable, and will likely need to be changed in order to make it work properly for bots running on a larger amount of guilds.