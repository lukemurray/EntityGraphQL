using System.Collections.Generic;
using System;

// This is a mock datamodel, what would be your real datamodel and EF context
namespace EntityGraphQL.Tests
{
    public class TestDataContext
    {
        public IEnumerable<Project> Projects { get; set; }
        public IEnumerable<Task> Tasks { get; set; }
        public IEnumerable<Location> Locations { get; set; }
        public IEnumerable<Person> People { get; set; }
    }

    public enum Gender
    {
        Female,
        Male,
        NotSpecified
    }

    public class Person
    {
        public int Id { get; set; }
        public Guid Guid { get; set; }
        public string Name { get; set; }
        public string LastName { get; set; }
        public Person Manager { get; set; }
        public Gender Gender { get; set; }
        public List<Project> Projects { get; set; }
        public List<Task> Tasks { get; set; }
        public DateTime? Birthday { get; set; }
    }

    public class Project
    {
        public int Id { get; set; }
        public int Type { get; set; }
        public Location Location { get; set; }
        public IEnumerable<Task> Tasks { get; set; }
        public Person Owner { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
    }

    public class Task
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool IsActive { get; set; }
        public Person Assignee { get; set; }
        public Project Project { get; set; }
    }
    public class Location
    {
        public int Id { get; set; }
        public string Address { get; set; }
        public string State { get; set; }
        public string Country { get; set; }
        public string Planet { get; set; }
    }
}