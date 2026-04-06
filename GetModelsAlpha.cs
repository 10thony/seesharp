using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;

namespace SeeSharp
{
    internal class GetModelsAlpha
    {
        private List<AIModel> models;

        public GetModelsAlpha()
        {
            init();
        }

        async void init()
        {

            string basePath = "http://cobec-spark:1234/v1";
            StringBuilder availableModelsSB = new StringBuilder();
            availableModelsSB.AppendLine("Select an available Models:");

            var models = await GetAvailableModelsAsync(new HttpClient(), new CancellationTokenSource().Token);

            foreach (AIModel model in models)
            {
                availableModelsSB.AppendLine($"({models.IndexOf(model)}): - {model.Id}");
            }


            Console.WriteLine(availableModelsSB.ToString());

            string userInput = Console.ReadLine();
            AIModel selectedModel = PromptUserToSelectModel(userInput);

            Console.WriteLine($"Selected Model: {selectedModel.Id}");
        }

        AIModel PromptUserToSelectModel(string userInput)
            {
                AIModel selectedModel = new AIModel();
                bool isUserInputValid =
                    int.TryParse(userInput, out int selectedModelIndex) &&
                    selectedModelIndex >= 0 &&
                    selectedModelIndex <= models.Count();


                if (isUserInputValid)
                {
                    return models[selectedModelIndex];
                }

                while (!isUserInputValid)
                {
                    Console.WriteLine("Invalid input. Please enter a valid number corresponding to the model you want to select:");
                    userInput = Console.ReadLine();
                    isUserInputValid = int.TryParse(userInput, out selectedModelIndex) &&
                    selectedModelIndex >= 0 &&
                    selectedModelIndex <= models.Count();

                }

                int.TryParse(userInput, out selectedModelIndex);

                return models[selectedModelIndex];
            }

            async Task<List<AIModel>> GetAvailableModelsAsync(HttpClient client, CancellationToken cancellationToken)
            {
                string uriValue = "http://cobec-spark:1234/v1/models";

                using var getModelRequest = new HttpRequestMessage(HttpMethod.Get, uriValue);

                using var response = await client.GetAsync(uriValue, cancellationToken);

                response.EnsureSuccessStatusCode();

                var modelsPayload = await response.Content.ReadFromJsonAsync<GetModelsResponse>(cancellationToken: cancellationToken);

                return modelsPayload?.data;

            }
        }



    #region Helper Classes

    //used by get models
    class GetModelsResponse
    {
        [JsonPropertyName("data")]
        public List<AIModel> data { get; set; }
    }
    class AIModel
    {
        public string Id { get; set; }
        public string @object { get; set; }
        public string Owned_by { get; set; }
    }

    #endregion
}
