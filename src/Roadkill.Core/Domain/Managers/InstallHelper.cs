﻿using System;
using System.Configuration;
using System.DirectoryServices;
using System.IO;
using System.Web;
using Roadkill.Core.Database;

namespace Roadkill.Core
{
	/// <summary>
	/// Provides a set of tasks for the Roadkill installer.
	/// </summary>
	internal class InstallHelper
	{
		private UserManager _userManager;
		private IRepository _repository;

		public InstallHelper(UserManager userManager, IRepository repository)
		{
			_userManager = userManager;
			_repository = repository;
		}

		/// <summary>
		/// Adds the admin user.
		/// </summary>
		/// <param name="summary">The settings to get the data from.</param>
		/// <exception cref="InstallerException">An NHibernate (database) error occurred while adding the admin user.</exception>
		public void AddAdminUser(SettingsSummary summary)
		{
			try
			{
				_userManager.AddUser(summary.AdminEmail, "admin", summary.AdminPassword, true, false);
			}
			catch (SecurityException ex)
			{
				throw new InstallerException(ex, "Failed to add the admin user");
			}
		}

		/// <summary>
		/// Resets the roadkill "installed" property in the web.config for when the installation fails.
		/// </summary>
		/// <exception cref="InstallerException">An web.config related error occurred while reseting the install state.</exception>
		public void ResetInstalledState()
		{
			try
			{
				System.Configuration.Configuration config = System.Web.Configuration.WebConfigurationManager.OpenWebConfiguration("~");

				RoadkillSection section = config.GetSection("roadkill") as RoadkillSection;
				section.Installed = false;

				config.Save();
			}
			catch (ConfigurationErrorsException ex)
			{
				throw new InstallerException(ex, "An exception occurred while resetting web.config install state to false.");
			}
		}

		/// <summary>
		/// Tests if the attachments folder provided can be written to, by writing a file to the folder.
		/// </summary>
		/// <param name="folder">The folder path which should include "~/" at the start.</param>
		/// <returns>Any error messages or an empty string if no errors occurred.</returns>
		public static string TestAttachments(string folder)
		{
			string errors = "";
			if (string.IsNullOrEmpty(folder))
			{
				errors = "The folder name is empty";
			}
			else
			{
				try
				{
					string directory = folder;
					if (folder.StartsWith("~"))
						directory = HttpContext.Current.Server.MapPath(folder);

					if (Directory.Exists(directory))
					{
						string path = Path.Combine(directory, "_installtest.txt");
						System.IO.File.WriteAllText(path, "created by the installer to test the attachments folder");
					}
					else
					{
						errors = "The directory does not exist, please create it first";
					}
				}
				catch (Exception e)
				{
					errors = e.ToString();
				}
			}

			return errors;
		}

		/// <summary>
		/// Tests the database connection.
		/// </summary>
		/// <param name="connectionString">The connection string.</param>
		/// <returns>Any error messages or an empty string if no errors occurred.</returns>
		public string TestConnection(string connectionString, string databaseType)
		{
			try
			{
				DataStoreType dataStoreType = DataStoreType.ByName(databaseType);
				if (dataStoreType == null)
					dataStoreType = DataStoreType.ByName("SQLServer2005");

				_repository = IoCSetup.ChangeRepository(dataStoreType, connectionString, false);

				System.Configuration.Configuration config = System.Web.Configuration.WebConfigurationManager.OpenWebConfiguration("~");
				RoadkillSection section = config.GetSection("roadkill") as RoadkillSection;

				// Only create the Schema if not already installed otherwise just a straight TestConnection
				bool createSchema = !section.Installed;
				_repository.Test(dataStoreType, connectionString);
				return "";
			}
			catch (Exception e)
			{
				return e.ToString();
			}
		}

		/// <summary>
		/// Tests the web.config can be saved to by changing the "installed" to false.
		/// </summary>
		/// <returns>Any error messages or an empty string if no errors occurred.</returns>
		public string TestSaveWebConfig()
		{
			try
			{
				ResetInstalledState();

				return "";
			}
			catch (Exception e)
			{
				return e.ToString();
			}
		}

		/// <summary>
		/// Tests a LDAP (Active Directory) connection.
		/// </summary>
		/// <param name="connectionString">The LDAP connection string (requires LDAP:// at the start).</param>
		/// <param name="username">The ldap username.</param>
		/// <param name="password">The ldap password.</param>
		/// <param name="groupName">The Active Directory group name to test against. Defaults to "Users" if empty</param>
		/// <returns>Any error messages or an empty string if no errors occurred.</returns>
		public string TestLdapConnection(string connectionString, string username, string password, string groupName)
		{
			if (string.IsNullOrEmpty(connectionString))
				return "The connection string is empty";

			try
			{
				int length = "ldap://".Length;
				if (!connectionString.StartsWith("LDAP://") || connectionString.Length < length)
					throw new Exception(string.Format("The LDAP connection string: '{0}' does not appear to be a valid (make sure it's uppercase LDAP).", connectionString));

				DirectoryEntry entry = new DirectoryEntry(connectionString);

				if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
				{
					entry.Username = username;
					entry.Password = password;
				}
				else
				{
					// Use built-in ones for querying
					username = "administrator"; // may need to use Guest here.
					groupName = "Users";
				}

				string accountName = username;
				string filter = "(&(objectCategory=user)(samAccountName=" + username + "))";

				if (!string.IsNullOrEmpty(groupName))
				{
					filter = "(&(objectCategory=group)(samAccountName=" + groupName + "))";
					accountName = groupName;
				}

				DirectorySearcher searcher = new DirectorySearcher(entry);
				searcher.Filter = filter;
				searcher.SearchScope = SearchScope.Subtree;

				SearchResult searchResult = searcher.FindOne();
				if (searchResult == null)
					return "Warning only: Unable to find " + accountName + " in the AD";
				else
					return "";
			}
			catch (Exception e)
			{
				return e.ToString();
			}
		}
	}
}
