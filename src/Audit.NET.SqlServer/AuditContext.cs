﻿namespace Audit.SqlServer
{
#if NET45
    using System.Data.Entity;

    internal class AuditContext : DbContext
    {

        public AuditContext(string connectionString, bool setNullInitializer)
            : base(connectionString)
        {
            if (setNullInitializer)
            {
                Database.SetInitializer<AuditContext>(null);
            }
        }
    }
#elif NETSTANDARD1_3 || NETSTANDARD2_0 || NETSTANDARD2_1
    using System.ComponentModel.DataAnnotations;
    using Microsoft.EntityFrameworkCore;
    
    internal class DynamicIdModel
    {
        [Key]
        public string Id { get; set; }

    }

    internal class AuditContext : DbContext
    {
        public DbSet<DynamicIdModel> FakeIdSet { get; set; }
        public string _connectionString;
        public AuditContext(string connectionString)
        {
            _connectionString = connectionString;
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseSqlServer(_connectionString);
        }
    }
#endif
}

