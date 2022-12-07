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

namespace Beyond
{
    public class BeyondDatabase
    {
        private Dictionary<string, string> _endpointStrings = new();
        private Dictionary<string, string> _tagStrings = new();
        private readonly CultureInfo _culture = new CultureInfo(ConfigurationManager.AppSettings["beyond-culture"] ?? "en-US");
        private readonly string _tableName = ConfigurationManager.AppSettings["beyond-table-name"] ?? "beyond";

        public AmazonDynamoDBClient Dynamo;

        public BeyondDatabase()
        {
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

        /// <summary>
        /// Registers an endpoint with the specified name, given the format string to create.
        /// Can use custom formatting based off the context when called with <c>GetEndpointString</c> or <c>GetTagString</c>
        /// <list type="table">
        ///     <listheader>
        ///         <term>format keyword</term>
        ///         <description>A list of eligible keywords for endpoint registration.</description>
        ///     </listheader>
        ///     <item>
        ///         <term>{user}</term>
        ///         <description>The ID of the user that invoked the interaction</description>
        ///     </item>
        ///     <item>
        ///         <term>{channel}</term>
        ///         <description>The ID of the channel that the interaction was invoked in</description>
        ///     </item>
        ///     <item>
        ///         <term>{guild}</term>
        ///         <description>The ID of the guild that the interaction was invoked in</description>
        ///     </item>
        ///     <item>
        ///         <term>{yearMonth}</term>
        ///         <description>The current date, formatted as year month, i.e. March 2022</description>
        ///     </item>
        /// </list>
        /// </summary>
        /// <param name="endpointName">The name of the endpoint you would like registered.</param>
        /// <param name="endpointFormatString">The format string for the endpoint.</param>
        public void AddEndpointString(string endpointName, string endpointFormatString, string tagFormatString)
        {
            if (_endpointStrings.ContainsKey(endpointName)) throw new Exception($"Endpoint \"{endpointName}\" is already registered within the database!");
            _endpointStrings[endpointName] = endpointFormatString;
            Console.WriteLine(_endpointStrings.Count);
            _tagStrings[endpointName] = tagFormatString;
        }

        /// <summary>
        /// Return the endpoint for the database given the specified context.
        /// This requires that you have already registered the context beforehand using <c>AddEndpointString</c>
        /// </summary>
        /// <param name="endpointName">The name of the endpoint you are trying to reach.</param>
        /// <param name="context">The context of the interaction that is trying to reach this endpoint.</param>
        /// 
        public string GetEndpointString(string endpointName, IInteractionContext context)
        {
            Console.WriteLine(_endpointStrings.Count);
            if (!_endpointStrings.ContainsKey(endpointName)) throw new Exception($"Could not find endpoint {endpointName}");
            var formatString = _endpointStrings[endpointName];
            var replacements = new Dictionary<string, object>
            {
                ["{user}"] = context.User.Id,
                ["{channel}"] = context.Channel.Id,
                ["{guild}"] = context.Guild.Id,
                ["{yearMonth}"] = DateTime.UtcNow.ToString("Y", _culture)
            };
            return replacements.Aggregate(formatString, (current, parameter) => current.Replace(parameter.Key, parameter.Value.ToString()));
        }

        /// <summary>
        /// Return the tag for the endpoint given the specified context.
        /// This requires that you have already registered the context beforehand using <c>AddEndpointString</c>
        /// </summary>
        /// <param name="endpointName">The name of the endpoint you are trying to reach.</param>
        /// <param name="context">The context of the interaction that is trying to reach this endpoint.</param>
        /// 
        public string GetTagString(string endpointName, IInteractionContext context)
        {
            if (!_tagStrings.ContainsKey(endpointName)) throw new Exception($"Could not find endpoint {endpointName}");
            var formatString = _tagStrings[endpointName];
            var replacements = new Dictionary<string, object>
            {
                ["{user}"] = context.User.Id,
                ["{channel}"] = context.Channel.Id,
                ["{guild}"] = context.Guild.Id,
                ["{yearMonth}"] = DateTime.UtcNow.ToString("Y", _culture)
            };
            return replacements.Aggregate(formatString, (current, parameter) => current.Replace(parameter.Key, parameter.Value.ToString()));
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
    }
}
