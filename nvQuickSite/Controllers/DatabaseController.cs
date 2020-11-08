﻿// Copyright (c) 2016-2020 nvisionative, Inc.
//
// This file is part of nvQuickSite.
//
// nvQuickSite is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// nvQuickSite is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with nvQuickSite.  If not, see <http://www.gnu.org/licenses/>.

namespace nvQuickSite.Controllers
{
    using System;
    using System.Data;
    using System.Data.SqlClient;

    /// <summary>
    /// Controls database operations.
    /// </summary>
    public class DatabaseController
    {
        private readonly string dbName;
        private readonly string dbServerName;
        private readonly bool usesWindowsAuthentication;
        private readonly string dbUserName;
        private readonly string dbPassword;
        private readonly string installFolder;
        private readonly bool usesSiteSpecificAppPool;
        private readonly object siteName;

        /// <summary>
        /// Initializes a new instance of the <see cref="DatabaseController"/> class.
        /// </summary>
        /// <param name="dbName">The name of the database.</param>
        /// <param name="dbServerName">The name of the database server.</param>
        /// <param name="usesWindowsAuthentication">A value indicating whether to use windows authentication for the database.</param>
        /// <param name="dbUserName">The database user name, ignored if using windows authentication.</param>
        /// <param name="dbPassword">The database password, ignored if using widows authentication.</param>
        /// <param name="installFolder">The path to the installation folder.</param>
        /// <param name="usesSiteSpecificAppPool">A value indicating whether the IIS site should use a dedicated App Pool.</param>
        /// <param name="siteName">The name of the website.</param>
        public DatabaseController(
            string dbName,
            string dbServerName,
            bool usesWindowsAuthentication,
            string dbUserName,
            string dbPassword,
            string installFolder,
            bool usesSiteSpecificAppPool,
            string siteName)
        {
            this.dbName = dbName;
            this.dbServerName = dbServerName;
            this.usesWindowsAuthentication = usesWindowsAuthentication;
            this.dbUserName = dbUserName;
            this.dbPassword = dbPassword;
            this.installFolder = installFolder;
            this.usesSiteSpecificAppPool = usesSiteSpecificAppPool;
            this.siteName = siteName;
        }

        /// <summary>
        /// Drops the existing database.
        /// </summary>
        /// <exception cref="DatabaseControllerException">Is thrown when the database could not be dropped.</exception>
        public void DropDatabase()
        {
            string myDBServerName = this.dbServerName;
            string connectionStringAuthSection = string.Empty;
            if (this.usesWindowsAuthentication)
            {
                connectionStringAuthSection = "Integrated Security=True;";
            }
            else
            {
                connectionStringAuthSection = "User ID=" + this.dbUserName + ";Password=" + this.dbPassword + ";";
            }

            using (SqlConnection myConn = new SqlConnection("Server=" + myDBServerName + "; Initial Catalog=master;" + connectionStringAuthSection))
            {
                string useMaster = @"USE master";
                string dropDatabase = $@"IF EXISTS(SELECT name FROM sys.databases WHERE name = '{this.dbName}') " +
                    $"DROP DATABASE [{this.dbName}]";

                SqlCommand useMasterCommand = new SqlCommand(useMaster, myConn);
                SqlCommand dropDatabaseCommand = new SqlCommand(dropDatabase, myConn);

                try
                {
                    myConn.Open();
                    useMasterCommand.ExecuteNonQuery();
                    dropDatabaseCommand.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    throw new DatabaseControllerException("Something when wrong while attempting to drop the database " + this.dbName, ex) { Source = "Drop Database" };
                }
                finally
                {
                    useMasterCommand.Dispose();
                    dropDatabaseCommand.Dispose();
                    if (myConn.State == ConnectionState.Open)
                    {
                        myConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Creates the database.
        /// </summary>
        public void CreateDatabase()
        {
            string connectionStringAuthSection = string.Empty;
            string connectionTimeout = "Connection Timeout=5;";
            if (this.usesWindowsAuthentication)
            {
                connectionStringAuthSection = "Integrated Security=True;";
            }
            else
            {
                connectionStringAuthSection = $"User ID={this.dbUserName};Password={this.dbPassword};";
            }

            using (SqlConnection myConn = new SqlConnection($"Server={this.dbServerName}; Initial Catalog=master;{connectionStringAuthSection}{connectionTimeout}"))
            {
                string str = $"CREATE DATABASE [{this.dbName}] ON PRIMARY " +
                    $"(NAME = [{this.dbName}_Data], " +
                    $"FILENAME = '{this.installFolder}\\Database\\{this.dbName}_Data.mdf', " +
                    "SIZE = 20MB, MAXSIZE = 200MB, FILEGROWTH = 10%) " +
                    $"LOG ON (NAME = [{this.dbName}_Log], " +
                    $"FILENAME = '{this.installFolder}\\Database\\{this.dbName}_Log.ldf', " +
                    "SIZE = 13MB, " +
                    "MAXSIZE = 50MB, " +
                    "FILEGROWTH = 10%)";

                SqlCommand sqlCommand = new SqlCommand(str, myConn);
                try
                {
                    myConn.Open();
                    sqlCommand.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    FileSystemController.RemoveDirectories(this.installFolder);
                    throw new DatabaseControllerException($"Error creating database {this.dbName}", ex) { Source = "Create Database" };
                }
                finally
                {
                    sqlCommand.Dispose();
                    if (myConn.State == ConnectionState.Open)
                    {
                        myConn.Close();
                    }
                }
            }
        }

        /// <summary>
        /// Sets the database permissions.
        /// </summary>
        internal void SetDatabasePermissions()
        {
            string connectionStringAuthSection = string.Empty;
            if (this.usesWindowsAuthentication)
            {
                connectionStringAuthSection = "Integrated Security=True;";
            }
            else
            {
                connectionStringAuthSection = $"User ID={this.dbUserName};Password={this.dbPassword};";
            }

            using (SqlConnection myConn = new SqlConnection($"Server={this.dbServerName}; Initial Catalog=master;{connectionStringAuthSection}"))
            {
                var appPoolNameFull = @"IIS APPPOOL\DefaultAppPool";
                var appPoolName = "DefaultAppPool";

                if (this.usesSiteSpecificAppPool)
                {
                    appPoolNameFull = $@"IIS APPPOOL\{this.siteName}_nvQuickSite";
                    appPoolName = $"{this.siteName}_nvQuickSite";
                }

                SqlCommand useMaster = new SqlCommand("USE master", myConn);
                SqlCommand grantLogin = new SqlCommand($"sp_grantlogin '{appPoolNameFull}'", myConn);
                SqlCommand useDb = new SqlCommand($"USE [{this.dbName}]", myConn);
                SqlCommand grantDbAccess = new SqlCommand($"sp_grantdbaccess '{appPoolNameFull}', '{appPoolName}'", myConn);
                SqlCommand addRoleMember = new SqlCommand($"sp_addrolemember 'db_owner', '{appPoolName}'", myConn);

                try
                {
                    myConn.Open();
                    useMaster.ExecuteNonQuery();
                    grantLogin.ExecuteNonQuery();
                    useDb.ExecuteNonQuery();
                    grantDbAccess.ExecuteNonQuery();
                    addRoleMember.ExecuteNonQuery();
                }
                catch (System.Exception ex)
                {
                    throw new DatabaseControllerException($"Error setting database permissions for database {this.dbName}", ex) { Source = "Set Database Permissions" };
                }
                finally
                {
                    useMaster.Dispose();
                    grantLogin.Dispose();
                    useDb.Dispose();
                    grantDbAccess.Dispose();
                    addRoleMember.Dispose();
                    if (myConn.State == ConnectionState.Open)
                    {
                        myConn.Close();
                    }
                }
            }
        }
    }
}