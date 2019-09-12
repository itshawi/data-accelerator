﻿// *********************************************************************
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License
// *********************************************************************

using ScenarioTester;
using System;
using DataX.ServerScenarios;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using DataX.Utilities.KeyVault;
using DataX.Utility.Blob;

namespace JobRunner.Jobs
{
    /// <summary>
    /// Runs through a steel thread scenario for a DataX every 10 minutes to ensure DataX E2E works for saving and deploying a job.
    /// </summary>
    public class DataXDeployJob : IJob
    {
        private readonly ScenarioDescription _scenario;
        private readonly AppConfig _config;
        private readonly ILogger _logger;
        private readonly int _scenarioCount = 1;

        public DataXDeployJob(AppConfig config, ILogger logger)
        {
            _config = config;
            _logger = logger;
            
            _scenario = new ScenarioDescription("DataXMainline",
                DataXHost.AcquireToken,
                DataXHost.SaveJob,
                DataXHost.GenerateConfigs,
                DataXHost.GetFlow,
                DataXHost.RestartJob
               );
        }
        /// <summary>
        /// This is the method that gets called when the job starts running
        /// </summary>
        /// <returns></returns>
        public async Task RunAsync()
        {
            var server = _config.ServiceUrl;
            var microsoftAuthority = _config.MicrosoftAuthority;
            var dataHubIdentifier = _config.DataHubIdentifier;
            if (string.IsNullOrWhiteSpace(server))
            {
                string errorMessage = "Server URL is not available.";
                _logger.LogError(_scenario.Description, "JobRunner ScenarioTester", new Dictionary<string, string>() { { "scenario.errorMessage", errorMessage } });                

                throw new InvalidOperationException(errorMessage);
            }

            using (var context = new ScenarioContext())
            {
                context[Context.ServiceUrl] = server;
                context[Context.ApplicationId] = KeyVault.GetSecretFromKeyvault(_config.ServiceKeyVaultName, _config.ApplicationId);                
                    
                // The flow config needs to be saved at this location
                string blobUri = $"{_config.BlobUri}";
                context[Context.FlowConfigContent] = await Task.Run(() => BlobUtility.GetBlobContent(KeyVault.GetSecretFromKeyvault(_config.BlobConnectionString), blobUri));                
                context[Context.DataHubIdentifier] = dataHubIdentifier;
                context[Context.SecretKey] = KeyVault.GetSecretFromKeyvault(_config.ServiceKeyVaultName, _config.SecretKey);                
                context[Context.MicrosoftAuthority] = microsoftAuthority;
                using (_logger.BeginScope<IReadOnlyCollection<KeyValuePair<string, object>>>(
                    new Dictionary<string, object> {
                        { "scenario.Description", _scenario.Description },
                        { "scenarioCount", _scenarioCount.ToString() },
                        { "scenario.Steps", $"[{string.Join(", ", _scenario.Steps.Select(s => s.Method.Name))}]" }
                    }))
                {
                    // do actual logging inside the scope. All logs inside this will have the properties from the Dictionary used in begin scope.
                    _logger.LogInformation("JobRunner ScenarioTester: " + _scenario.Description);

                }

                var results = await ScenarioResult.RunAsync(_scenario, context, _scenarioCount);
                int iterationCount = 0;

                foreach (var result in results)
                {
                    string scenarioResult = result.Failed ? "failed" : "succeeded";
                   
                    // log failed steps.
                    foreach (var stepResult in result.StepResults.Where(r => !r.Success))
                    {
                        using (_logger.BeginScope<IReadOnlyCollection<KeyValuePair<string, object>>>(
                            new Dictionary<string, object> {
                                { "Scenario iteration", $"Scenario iteration {_scenario.Description}.{iterationCount} " },
                                { "ScenarioResult length", scenarioResult.Length}
                            }))
                        {
                            // do actual logging inside the scope. All logs inside this will have the properties from the Dictionary used in begin scope.
                            _logger.LogInformation(_scenario.Description);

                        }                        
                        
                        if (stepResult.Exception != null)
                        {
                            _logger.LogError(stepResult.Exception, _scenario.Description);
                        }
                        _logger.LogError(stepResult.Value);
                    }

                    iterationCount++;
                }

                //emit metric on how many parallel executions passed.
                using (_logger.BeginScope<IReadOnlyCollection<KeyValuePair<string, object>>>(
                    new Dictionary<string, object> {
                        { "SuccessRate", $"{(long)((double)results.Count(r => !r.Failed) / _scenarioCount * 100.0)}" }
                    }))
                    {
                        // do actual logging inside the scope. All logs inside this will have the properties from the Dictionary used in begin scope.
                        _logger.LogInformation(_scenario.Description);

                    }
            }
        }
    }
}
