using './main.bicep'

// All parameter values are passed via the deploy script (deploy-pokemonircbot.sh)
// so that no real resource names or environment-specific values are committed to git.
//
// Required params (passed by the script):
//   appName             — App Service name (becomes <name>.azurewebsites.net)
//   storageAccountName  — Storage account name for season stats blobs
//   ircChannel          — IRC channel the bot joins
//
// Optional params (have defaults in main.bicep):
//   location            — Azure region
//   seasonId            — Season identifier
//   seasonName          — Season display name
//   deployerObjectId    — passed by script at deploy time
