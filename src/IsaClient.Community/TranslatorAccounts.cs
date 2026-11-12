using System;
using System.Collections.Generic;
using System.Linq;
using IsaClient.Core;

namespace IsaClient.Community;

/// <summary>
/// MyMemory gives every e-mail its own daily quota of free translations. Each install picks a random simple Spanish
/// word and uses "word@gmail.com"; when that one runs out for the day another word is picked (and remembered).
/// </summary>
public static class TranslatorAccounts
{
	/// <summary>The word of this install ("" = not picked yet).</summary>
	[Setting("TranslatorWord")]
	public static string Word = "";

	private static readonly string[] Words =
	{
		"casa", "perro", "gato", "arbol", "flor", "mesa", "silla", "puerta", "ventana", "libro",
		"lapiz", "papel", "agua", "fuego", "tierra", "viento", "nube", "lluvia", "nieve", "sol",
		"luna", "estrella", "cielo", "mar", "rio", "lago", "playa", "arena", "piedra", "monte",
		"bosque", "campo", "camino", "puente", "pueblo", "ciudad", "calle", "plaza", "parque", "jardin",
		"manzana", "pera", "uva", "fresa", "naranja", "limon", "banana", "mango", "cereza", "sandia",
		"melon", "durazno", "coco", "pan", "queso", "leche", "huevo", "arroz", "sopa", "pastel",
		"galleta", "miel", "azucar", "sal", "cafe", "jugo", "taza", "plato", "vaso", "cuchara",
		"tenedor", "olla", "horno", "cama", "almohada", "manta", "espejo", "reloj", "lampara", "vela",
		"llave", "caja", "bolsa", "cesta", "globo", "pelota", "juguete", "muneca", "trompo", "cometa",
		"barco", "tren", "avion", "coche", "moto", "bici", "cohete", "camion", "carro", "velero",
		"pato", "pollo", "gallina", "vaca", "toro", "caballo", "burro", "oveja", "cabra", "cerdo",
		"conejo", "raton", "zorro", "lobo", "oso", "tigre", "leon", "mono", "jirafa", "cebra",
		"elefante", "delfin", "ballena", "tiburon", "pez", "pulpo", "cangrejo", "tortuga", "rana", "sapo",
		"abeja", "hormiga", "mariposa", "grillo", "arana", "caracol", "gusano", "pajaro", "loro", "buho",
		"aguila", "paloma", "cuervo", "pinguino", "foca", "koala", "panda", "ardilla", "erizo", "castor",
		"rojo", "azul", "verde", "amarillo", "blanco", "negro", "rosa", "morado", "gris", "dorado",
		"plata", "oro", "cobre", "hierro", "cristal", "diamante", "perla", "rubi", "zafiro", "esmeralda",
		"musica", "canto", "baile", "guitarra", "piano", "tambor", "flauta", "violin", "campana", "silbato",
		"lunes", "martes", "jueves", "viernes", "sabado", "domingo", "enero", "febrero", "marzo", "abril",
		"mayo", "junio", "julio", "agosto", "octubre", "otono", "verano", "invierno", "primavera", "manana",
		"tarde", "noche", "dia", "hora", "minuto", "semana", "mes", "ano", "siglo", "momento",
		"amigo", "amiga", "hermano", "hermana", "primo", "prima", "abuelo", "abuela", "vecino", "maestro",
		"doctor", "piloto", "pintor", "cartero", "panadero", "granjero", "marinero", "bombero", "cocinero", "artista",
		"mano", "pie", "cara", "ojo", "boca", "nariz", "oreja", "pelo", "brazo", "rodilla",
		"sombrero", "zapato", "camisa", "falda", "abrigo", "bufanda", "guante", "bota", "gorra", "chaqueta",
		"nido", "hoja", "rama", "raiz", "semilla", "hierba", "cactus", "palma", "pino", "roble",
		"isla", "volcan", "desierto", "selva", "cueva", "valle", "colina", "costa", "ola", "marea",
		"trueno", "rayo", "niebla", "brisa", "arcoiris", "aurora", "cometa", "planeta", "galaxia", "eclipse",
		"alegria", "risa", "suerte", "sueno", "magia", "aventura", "tesoro", "mapa", "brujula", "linterna",
		"castillo", "torre", "muralla", "molino", "faro", "granja", "establo", "tienda", "mercado", "escuela",
		"cuaderno", "regla", "goma", "pizarra", "tiza", "mochila", "carpeta", "sobre", "carta", "postal",
		"dado", "carta", "ficha", "tablero", "puzzle", "cubo", "rueda", "cuerda", "escalera", "martillo",
		"clavo", "tornillo", "sierra", "pala", "rastrillo", "cubeta", "regadera", "maceta", "valla", "buzon",
		"helado", "chocolate", "caramelo", "dulce", "turron", "flan", "batido", "yogur", "tostada", "empanada",
		"tomate", "papa", "cebolla", "ajo", "zanahoria", "lechuga", "pepino", "maiz", "frijol", "calabaza",
		"pluma", "cola", "ala", "pico", "garra", "concha", "escama", "cuerno", "huella", "nudo"
	};

	/// <summary>Words that ran out this session (the quota resets the next day, so this isn't saved).</summary>
	private static readonly HashSet<string> _spent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	private static readonly Random _random = new Random();

	/// <summary>"word@gmail.com" of this install; picks the word the first time.</summary>
	public static string Email
	{
		get
		{
			lock (_spent)
			{
				if (string.IsNullOrWhiteSpace(Word))
				{
					Word = Pick();
					SaveLater();
				}
				return Word + "@gmail.com";
			}
		}
	}

	/// <summary>The current word ran out: take another one. False when every word ran out today.</summary>
	public static bool Rotate()
	{
		lock (_spent)
		{
			if (!string.IsNullOrWhiteSpace(Word))
			{
				_spent.Add(Word);
			}
			string next = Pick();
			if (next == null)
			{
				return false;
			}
			Word = next;
			SaveLater();
			return true;
		}
	}

	/// <summary>Translations run on a worker thread; the settings file is written by the game thread.</summary>
	private static void SaveLater()
	{
		Terraria.Main.QueueMainThreadAction(ClientSettings.Save);
	}

	private static string Pick()
	{
		string[] free = Words.Distinct().Where(w => !_spent.Contains(w)).ToArray();
		return (free.Length == 0) ? null : free[_random.Next(free.Length)];
	}
}
