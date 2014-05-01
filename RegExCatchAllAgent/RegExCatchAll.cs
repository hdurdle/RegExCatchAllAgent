/*
 * An Exchange 2007, 2010 and 2013 Transport Protocol agent that implements catch
 * all for multiple addresses via regular expressions and also includes a recipient
 * ban list.
 * 
 * Howard Durdle 
 * http://twitter.com/hdurdle
 * 
 */

// Enable tracing of the configuration processing.
#define TRACE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using Microsoft.Exchange.Data.Transport;
using Microsoft.Exchange.Data.Transport.Smtp;

namespace RegExCatchAllAgent
{
    /// <summary>
    /// CatchAllFactory: The agent factory for the CatchAllAgent.
    /// </summary>
    public sealed class CatchAllFactory : SmtpReceiveAgentFactory
    {
        private readonly CatchAllConfig _catchAllConfig = new CatchAllConfig();

        /// <summary>
        /// Creates a CatchAllAgent instance.
        /// </summary>
        /// <param name="server">The SMTP server.</param>
        /// <returns>An agent instance.</returns>
        public override SmtpReceiveAgent CreateAgent(SmtpServer server)
        {
            return new CatchAllAgent(_catchAllConfig, server.AddressBook);
        }
    }

    /// <summary>
    /// CatchAllAgent: An SmtpReceiveAgent implementing catch-all.
    /// </summary>
    public class CatchAllAgent : SmtpReceiveAgent
    {
        /// <summary>
        /// The SMTP response to be sent for rejected messages.
        /// </summary>
        private static readonly SmtpResponse RejectResponse =
            new SmtpResponse("500", "", "Recipient blocked");

        /// <summary>
        /// The address book to be used for lookups.
        /// </summary>
        private readonly AddressBook _addressBook;

        /// <summary>
        /// The configuration
        /// </summary>
        private readonly CatchAllConfig _catchAllConfig;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="catchAllConfig">The configuration.</param>
        /// <param name="addressBook">The address book to perform lookups.</param>
        public CatchAllAgent(CatchAllConfig catchAllConfig, AddressBook addressBook)
        {
            // Save the address book and configuration
            _addressBook = addressBook;
            _catchAllConfig = catchAllConfig;

            // Register an OnRcptCommand event handler
            OnRcptCommand += RcptToHandler;
        }

        /// <summary>
        /// Handles the "RCPT TO:" SMTP command
        /// </summary>
        /// <param name="source">The event source.</param>
        /// <param name="rcptArgs"></param>
        public void RcptToHandler(ReceiveCommandEventSource source, RcptCommandEventArgs rcptArgs)
        {
            // Get the recipient address as a lowercase string.
            string strRecipientAddress = rcptArgs.RecipientAddress.ToString().ToLower();

            // Search the banned email List for the recipient.
            bool exists = _catchAllConfig.Banned.Exists(element => element == strRecipientAddress);
            if (exists)
            {
                // If found respond to the sending MTA with the reject response.
                source.RejectCommand(RejectResponse);

                // No further processing.
                return;
            }

            // For each pair of regexps to email addresses
            foreach (var pair in _catchAllConfig.AddressMap)
            {
                // Create the regular expression and the routing address from the dictionary.
                var emailPattern = new Regex(pair.Key);
                RoutingAddress emailAddress = pair.Value;

                // If the recipient address matches the regular expression.
                if (emailPattern.IsMatch(strRecipientAddress))
                {
                    // And if the recipient is NOT in the address book.
                    if ((_addressBook != null) && (_addressBook.Find(rcptArgs.RecipientAddress) == null))
                    {
                        // Redirect the recipient to the other address.
                        rcptArgs.RecipientAddress = emailAddress;

                        // No further processing.
                        return;
                    }
                }
            }

            // No further processing.
            return;
        }
    }


    /// <summary>
    /// CatchAllConfig: The configuration for the CatchAllAgent.
    /// </summary>
    public class CatchAllConfig
    {
        /// <summary>
        ///  The name of the configuration file.
        /// </summary>
        private const string ConfigFileName = "config.xml";

        /// <summary>
        /// Point out the directory with the configuration file (= assembly location)
        /// </summary>
        private readonly string _configDirectory;

        /// <summary>
        /// The filesystem watcher to monitor configuration file updates.
        /// </summary>
        private readonly FileSystemWatcher _configFileWatcher;

        /// <summary>
        /// The (pattern to) catchall address map
        /// </summary>
        private Dictionary<string, RoutingAddress> _addressMap;

        /// <summary>
        /// Whether reloading is ongoing
        /// </summary>
        private int _reloading;

        /// <summary>
        /// Constructor.
        /// </summary>
        public CatchAllConfig()
        {
            // Setup a file system watcher to monitor the configuration file
            _configDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (_configDirectory != null)
            {
                _configFileWatcher = new FileSystemWatcher(_configDirectory)
                {
                    NotifyFilter = NotifyFilters.LastWrite,
                    Filter = "config.xml"
                };
            }
            _configFileWatcher.Changed += OnChanged;

            // Create an initially empty map
            _addressMap = new Dictionary<string, RoutingAddress>();

            // Load the configuration
            Load();

            // Now start monitoring
            _configFileWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// The mapping between pattern to catchall address.
        /// </summary>
        public Dictionary<string, RoutingAddress> AddressMap
        {
            get { return _addressMap; }
        }

        /// <summary>
        /// List for banned emails.
        /// </summary>
        List<string> _banned;

        /// <summary>
        /// List property getter.
        /// </summary>
        public List<string> Banned
        {
            get { return _banned; }
        }

        /// <summary>
        /// Configuration changed handler.
        /// </summary>
        /// <param name="source">Event source.</param>
        /// <param name="e">Event arguments.</param>
        private void OnChanged(object source, FileSystemEventArgs e)
        {
            // Ignore if load ongoing
            if (Interlocked.CompareExchange(ref _reloading, 1, 0) != 0)
            {
                Trace.WriteLine("Already loading. Ignoring.");
                return;
            }

            // (Re) Load the configuration
            Load();

            // Reset the reload indicator
            _reloading = 0;
        }

        /// <summary>
        /// Load the configuration file. If any errors occur, does nothing.
        /// </summary>
        private void Load()
        {
            Trace.WriteLine("");
            Trace.WriteLine(String.Format("{0:s} Configuration Change. Reloading.", DateTime.Now));

            // Load the configuration
            var doc = new XmlDocument();
            bool docLoaded = false;
            string fileName = Path.Combine(
                _configDirectory,
                ConfigFileName);

            try
            {
                doc.Load(fileName);
                docLoaded = true;
            }
            catch (FileNotFoundException)
            {
                Trace.WriteLine("Configuration file not found: {0}", fileName);
            }
            catch (XmlException e)
            {
                Trace.WriteLine("XML error: {0}", e.Message);
            }
            catch (IOException e)
            {
                Trace.WriteLine("IO error: {0}", e.Message);
            }

            // If a failure occured, ignore and simply return
            if (!docLoaded || doc.FirstChild == null)
            {
                Trace.WriteLine("Configuration error: either no file or XML error.");
                return;
            }

            // Create a dictionary to hold the mappings
            var map = new Dictionary<string, RoutingAddress>(100);

            // List to hold banned emails
            var banned = new List<string>();

            // Track whether there are invalid entries
            bool invalidEntries = false;

            // get all the <banned ... /> elements
            foreach (XmlNode node in doc.GetElementsByTagName("banned"))
            {
                // Get the banned email address
                XmlAttribute address = node.Attributes["address"];

                // Is the address valid as an SMTP address?
                if (!RoutingAddress.IsValidAddress(address.Value))
                {
                    invalidEntries = true;
                    Trace.WriteLine(String.Format("Reject configuration due to invalid ban address: {0}", address));
                    break;
                }

                // Add the string to the List
                banned.Add(address.Value);
                Trace.WriteLine(String.Format(" Banning: {0}", address.Value));
            }

            // get all the <redirect ... /> elements
            foreach (XmlNode node in doc.GetElementsByTagName("redirect"))
            {
                // Get the regex pattern and the redirect email address
                XmlAttribute pattern = node.Attributes["pattern"];
                XmlAttribute address = node.Attributes["address"];

                // Validate the data
                if (pattern == null || address == null)
                {
                    invalidEntries = true;
                    Trace.WriteLine("Reject configuration due to incomplete entry. (Either or both pattern and address missing.)");
                    break;
                }

                // Is the redirect address valid as an SMTP address?
                if (!RoutingAddress.IsValidAddress(address.Value))
                {
                    invalidEntries = true;
                    Trace.WriteLine(String.Format("Reject configuration due to invalid redirect address: {0}", address));
                    break;
                }

                // Add the new entry
                string strPattern = pattern.Value;
                map[strPattern] = new RoutingAddress(address.Value);

                Trace.WriteLine(String.Format("Redirect: {0} -> {1}", strPattern, address.Value));
            }

            // If there are no invalid entries, swap in the map
            if (!invalidEntries)
            {
                Interlocked.Exchange(ref _addressMap, map);
                Interlocked.Exchange(ref _banned, banned);
                Trace.WriteLine("Configuration Accepted.");
            }
        }
    }
}