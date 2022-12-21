using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Amazon.Runtime.CredentialManagement;
using Amazon.Runtime;
using Discord;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Discord.WebSocket;

namespace Beyond
{
    public class BeyondDatabase
    {
        private readonly CultureInfo _culture = new CultureInfo(ConfigurationManager.AppSettings["beyond-culture"] ?? "en-US");
        private readonly string _tableName = ConfigurationManager.AppSettings["beyond-table-name"] ?? "beyond";
        private readonly DiscordSocketClient _client;

        public AmazonDynamoDBClient Dynamo;

        public BeyondDatabase(DiscordSocketClient client)
        {
            _client = client;
            // Setting Amazon DynamoDB integration.
            var profileName = ConfigurationManager.AppSettings["beyond-profile"] ?? "Beyond";
            var chain = new CredentialProfileStoreChain();
            AWSCredentials credentials;
            if (!chain.TryGetAWSCredentials(profileName, out credentials)) throw new Exception("Could not get AWS profile from beyond-profile!");
            var config = new AmazonDynamoDBConfig()
            {
                RegionEndpoint = Amazon.RegionEndpoint.USEast1
            };
            Dynamo = new AmazonDynamoDBClient(credentials, config);
        }

        // Returns a short date equivalent - based off of the culture types.
        public string GetShortDate(DateTime time) => time.ToString("Y", _culture);

        public Task<GetItemResponse> GetItemAsync(GetItemRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.GetItemAsync(request);
        }

        public Task<PutItemResponse> PutItemAsync(PutItemRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.PutItemAsync(request);
        }

        public Task<UpdateItemResponse> UpdateItemAsync(UpdateItemRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.UpdateItemAsync(request);
        }

        public Task<QueryResponse> QueryAsync(QueryRequest request)
        {
            request.TableName = _tableName;
            return Dynamo.QueryAsync(request);
        }

        public async Task<Dictionary<string, AttributeValue>> GetGuildInformation(IGuild guild)
        {
            GetItemResponse getResponse;
            var getRequest = new GetItemRequest
            {
                Key =
                {
                    ["guild"] = new AttributeValue {N = guild.Id.ToString() },
                    ["tag"] = new AttributeValue {S = "information" }
                }
            };
            getResponse = await GetItemAsync(getRequest);
            getResponse.Item["guild"] = new AttributeValue { N = guild.Id.ToString() };
            getResponse.Item["tag"] = new AttributeValue { S = "information" };
            await BeyondBot.Instance.VerifyGuild(getResponse.Item, _client.GetGuild(guild.Id));            
            return getResponse.Item;
        }
    }
}
