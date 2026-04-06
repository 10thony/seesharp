Resources
https://lmstudio.ai/docs/developer/openai-compat
https://www.mihaileric.com/The-Emperor-Has-No-Clothes/ 
https://developers.openai.com/api/reference/resources/responses

SeeSharp is a minimalist Coding Agent who's main support is centered around self hosted LLMs as opposed to the traditional LLM Providers.
It has three simple tools, Read File, Edit File, List Files. This is based off of https://www.mihaileric.com/The-Emperor-Has-No-Clothes/
Naturally, as a .NET developer, I've taken something that's 200~ lines and made it a much longer read!
However I felt as someone who finds alot of joy in being a Prompter (I actually just like to talk), I find it imperative to still have projects where I write the code by hand. Its a drag I know, but as a father, one of the most important lessons we bestow upon children is the necessity to do hard, annoying repetitive work. Not because it provides immediate value but because it builds up that resistance to boredom, to choosing the easy way out.
First lets review the Tech Stack,
This is a .net 10 Console project that connects to an nvidia spark and local models hosted on the Spark via LMStudio. For the sake of keeping this blog concise we will just persist chats via a JSON file. Stay tuned for a future blog where we integrate a Convex backend via the unofficial-dotnet-convex client. For those following along, you can use your own localhost or another piece of hardware on your network/tailnet. Or follow Mr.Mihail Eric's lead and use a standard LLM Provider.
Let's first review what the expected output is when we request all the models from LMS studio on my hardware at http://cobec-spark:1234/v1/models. Then with writing a function to get all the available models hosted on the hardware.

The response from v1/models is 
{
  "data": [
    {
      "id": "qwen/qwen3.5-9b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "qwen/qwen3.5-4b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "qwen/qwen3.5-2b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "google/gemma-3-4b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "google/gemma-3-1b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "google/gemma-3-270m",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "openai/gpt-oss-120b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "openai/gpt-oss-20b",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "qwen/qwen3-coder-next",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "microsoft/phi-4-reasoning-plus",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "microsoft/phi-4",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "microsoft/phi-4-mini-reasoning",
      "object": "model",
      "owned_by": "organization_owner"
    },
    {
      "id": "text-embedding-nomic-embed-text-v1.5",
      "object": "model",
      "owned_by": "organization_owner"
    }
  ],
  "object": "list"
}


Our method is straight forward, we make a Get request, parse the response and return it as a list we'll store locally
@/getavailablemodels.png

There are two helper classes, one to represent the Response from the HTTPGet of the models, 
the second a class to represent the AIModel available for use.
@/getmodelshelpermethods.png


