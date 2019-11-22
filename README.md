# This is a LINQPad driver for MongoDB

Requirement
-------------
* LINQPad 6.x, 
* .net core 3.x

Installation
-------------
1. Click `Add connection`
2. Click `View more drivers...`
3. On LINQPad Nuget Manager window, switch the radio button to `Search all drivers`
4. Select package `MongoDB LINQPad driver` and click `install`
 
Setup connection
-------------
1. Add connection, choose `Build data context automatically` and select MongoDB Driver click `Next`.
2. Configure some connection information.
> Because MongoDB document is type-less if you want to use the strong-typed document you need to tell the driver where are the type definitions(`Path to typed documents assembly`) and which namespace's types will be used.

**Note**
> The collection type will be exposed as ```IMongoCollection<BsonDocument>``` if no type named as collection name had been found in the assembly.


