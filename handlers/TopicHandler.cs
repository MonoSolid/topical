namespace topical.handlers;

/// <summary>
/// Topic update handler
/// </summary>
/// <typeparam name="T">Update type</typeparam>
public delegate void TopicHandler<in T>(T update);