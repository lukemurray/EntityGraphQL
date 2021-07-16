using System;
using System.Collections.Generic;
using System.ComponentModel;
using EntityGraphQL.Schema;
using Microsoft.EntityFrameworkCore;

namespace demo
{
    public class DemoContext : DbContext
    {
        public DemoContext(DbContextOptions<DemoContext> opts) : base(opts)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);
        }


        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<Actor>().HasKey(d => d.PersonId);
            builder.Entity<Writer>().HasKey(d => d.PersonId);
            builder.Entity<Movie>().HasOne(d => d.Director).WithMany(p => p.DirectorOf).HasForeignKey(d => d.DirectorId);
            builder.Entity<Person>().HasMany(p => p.ActorIn).WithOne(a => a.Person);
            builder.Entity<Person>().HasMany(p => p.WriterOf).WithOne(a => a.Person);
        }

        [Description("Collection of Movies")]
        public DbSet<Movie> Movies { get; set; }
        [Description("Collection of Peoples")]
        public DbSet<Person> People { get; set; }
        [Description("Collection of Actors")]
        public DbSet<Actor> Actors { get; set; }
    }

    public class Movie
    {
        public uint Id { get; set; }
        public string Name { get; set; }

        [Description("Enum of Genre")]
        public Genre Genre { get; set; }
        public DateTime Released { get; set; }
        public List<Actor> Actors { get; set; }
        public List<Writer> Writers { get; set; }
        public Person Director { get; set; }
        public uint? DirectorId { get; set; }
        public double Rating { get; internal set; }
    }

    public class Actor
    {
        public uint PersonId { get; set; }
        public Person Person { get; set; }
        public uint MovieId { get; set; }
        public Movie Movie { get; set; }
    }
    public class Writer
    {
        public uint PersonId { get; set; }
        public Person Person { get; set; }
        public uint MovieId { get; set; }
        public Movie Movie { get; set; }
    }

    public enum Genre
    {
        [Description("Action movie type")]
        Action,
        [Description("Drama movie type")]
        Drama,
        [Description("Comedy movie type")]
        Comedy,
        [Description("Horror movie type")]
        Horror,
        [Description("Scifi movie type")]
        Scifi,
    }

    public class Person
    {
        public uint Id { get; set; }
        [GraphQLNotNull]
        public string FirstName { get; set; }
        [GraphQLNotNull]
        public string LastName { get; set; }
        public DateTime Dob { get; set; }
        public List<Actor> ActorIn { get; set; }
        public List<Writer> WriterOf { get; set; }
        public List<Movie> DirectorOf { get; set; }
        public DateTime? Died { get; set; }
        public bool IsDeleted { get; set; }
    }
}