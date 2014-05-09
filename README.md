RegExCatchAllAgent
==================

An Exchange 2007 and 2010 Transport Protocol agent that implements catch
all for multiple addresses via regular expressions and also includes a recipient
ban list.

- Howard Durdle ([@hdurdle][twitter])
- http://durdle.com

With thanks to Wilbert De Graaf (wilbertdg@hotmail.com) whose CatchAllAgent work
saved me a lot of time!

Usage:
-----

1. Copy both the assembly and `config.xml` file to the same location on your 
   Exchange 2007 Edge or (Internet facing) Hub role.

2. Add entries to the `config.xml` for the addresses for which you want catch 
   all to be working.  
   Entries look like this:

        <config>
            <redirect pattern="^john+\.[a-z]+@domain.com$" address="john@domain.com" />
            <redirect pattern="^jane+\.[a-z]+@domain.com$" address="jane.doe@gmail.com" />
            <banned address="john.spam@domain.com" />
            <banned address="jane.newsletter@domain.com" />
        </config>

   The regular expressions in the example allow for a custom catchall address
   of john.uniqueword@domain.com.

   This way the user can give out a custom email address for websites they
   don't trust with their real address. If that email address is abused, it
   can be added to the ban list so the user won't receive those messages.
   You can create your own regular expressions, but carefully test them first!

   Note: The messages will be redirected to the specified 'address' and 
   it's important that those addresses really exist. Otherwise the message
   will NDR, or Recipient Filtering will reject the message.

   Banned addresses are processed first, so if a recipient address matches 
   one from the ban list it will get rejected. The sender will receive a 
   500 SMTP code and the connection will be dropped.

   The email patterns use standard .NET regular expressions and are 
   processed in order they appear in the `config.xml`.
   If a recipient address matches more than one regexp it will be 
   redirected to the first match and no further processing will take place.

3. Install the agent and set its priority so it is above the recipient filter.

   It's possible to dump traces to a file (`traces.log` in the example below) 
   by adding the following to the application configuration file (`edgetransport.exe.config`):


        <configuration>
          <system.diagnostics>
            <trace autoflush="false" indentsize="4">
              <listeners>
                <add name="myListener" 
                 type="System.Diagnostics.TextWriterTraceListener" 
                 initializeData="traces.log" />
                <remove name="Default" />
              </listeners>
            </trace>
          </system.diagnostics>
        </configuration>

[twitter]:[http://twitter.com/hdurdle]
