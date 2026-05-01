Study the @Dev/LocalTestProjectMenu.cs  we need to create a seperate test menu.

heres the highlevel view, we instead of testing one agent on one test project at a time 

this time, we are going to have a Dictionary <string,List<string>> that represents the relationship of test project paths and the related list.

the list of test project directories should be pulled from the test-projects directory, and the lists should just start off as empty lists for devs to go and hardcode in.
