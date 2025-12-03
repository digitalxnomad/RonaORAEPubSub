
@echo off
SET SOLUTION_NAME=PubSubHighThroughput
SET PROJECT_NAME=PubSubApp

dotnet new sln -n %SOLUTION_NAME%
dotnet new console -n %PROJECT_NAME%
dotnet sln %SOLUTION_NAME%.sln add %PROJECT_NAME%\%PROJECT_NAME%.csproj

dotnet add %PROJECT_NAME% package Google.Cloud.PubSub.V1

echo Project setup complete.
