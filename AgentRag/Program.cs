using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Encodings.Web;
using System.Text.Json;
using AgentRag;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

TextSearchProviderOptions textSearchOptions = new()
{
    SearchTime = TextSearchProviderOptions.TextSearchBehavior.BeforeAIInvoke,
    //RecentMessageMemoryLimit = 6
    //CitationsPrompt = "When you use information from the context, include citations in the format: [SourceName](SourceLink)",
    //ContextFormatter = results =>
    //{
    //    var sb = new StringBuilder();
    //    sb.AppendLine("## Additional Context");
    //    sb.AppendLine("Use this information to answer. Add citations as: [SourceName](SourceLink).");
    //    sb.AppendLine();

    //    for (var i = 0; i < results.Count; i++)
    //    {
    //        var r = results[i];
    //        var name = string.IsNullOrWhiteSpace(r.SourceName) ? $"Source {i + 1}" : r.SourceName;
    //        var link = r.SourceLink;

    //        // Header that makes the intended citation format unambiguous
    //        if (!string.IsNullOrWhiteSpace(link))
    //        {
    //            sb.AppendLine($"- Citation: [{name}]({link})");
    //        }
    //        else
    //        {
    //            sb.AppendLine($"- Citation: {name}");
    //        }

    //        sb.AppendLine("  Excerpt:");
    //        sb.AppendLine($"  {r.Text}");
    //        sb.AppendLine();
    //    }

    //    return sb.ToString();
    //}
    //ContextFormatter = results =>
    //{
    //    var sb = new StringBuilder();

    //    sb.AppendLine("## Additional Context");
    //    sb.AppendLine("Use the excerpts below to answer the user.");
    //    sb.AppendLine("Citation rules:");
    //    sb.AppendLine("- Do NOT add inline citations.");
    //    sb.AppendLine("- At the END of your answer, add a single line exactly like:");
    //    sb.AppendLine("  Sources: [SourceName](SourceLink), [SourceName](SourceLink)");
    //    sb.AppendLine("- Include ONLY sources you actually used. No duplicates.");
    //    sb.AppendLine();

    //    sb.AppendLine("### Sources (copy/paste-ready)");
    //    foreach (var (i, r) in results.Index())
    //    {
    //        var name = string.IsNullOrWhiteSpace(r.SourceName) ? $"Source {i + 1}" : r.SourceName;

    //        if (!string.IsNullOrWhiteSpace(r.SourceLink))
    //        {
    //            sb.AppendLine($"- [{name}]({r.SourceLink})");
    //        }
    //        else
    //        {
    //            sb.AppendLine($"- {name}");
    //        }
    //    }

    //    sb.AppendLine();

    //    sb.AppendLine("### Excerpts");
    //    foreach (var (i, r) in results.Index())
    //    {
    //        var name = string.IsNullOrWhiteSpace(r.SourceName) ? $"Source {i + 1}" : r.SourceName;

    //        sb.AppendLine($"[{i + 1}] {name}");
    //        sb.AppendLine(r.Text);
    //        sb.AppendLine();
    //    }

    //    return sb.ToString();
    //}
};

var openAIClient = new OpenAIClient(new ApiKeyCredential(Constants.ApiKey), new()
{
    Endpoint = new(Constants.Endpoint),
    Transport = new HttpClientPipelineTransport(new HttpClient(new TraceHttpClientHandler()))
}).GetChatClient(Constants.DeploymentName).AsIChatClient();

var reformulationChatHistoryProvider = new InMemoryChatHistoryProvider(new()
{
    StorageInputRequestMessageFilter = messages => [],
    StorageInputResponseMessageFilter = messages => []
});

var reformulationAgent = openAIClient
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "reformulation-agent"
        ChatOptions = new()
        {
            Instructions = """
                You are a helpful assistant that reformulates questions to perform embeddings search.
                Your task is to reformulate the question taking into account the context of the chat.
                The reformulated question must always explicitly contain the subject of the question.

                You MUST reformulate the question in the SAME language as the user's question.
                For example, if the user asks a question in English, the reformulated question MUST be in English. If the user asks in Italian, the reformulated question MUST be in Italian.

                Never add "in this chat", "in the context of this chat", "in the context of our conversation", "search for" or something like that in your answer.
                Your answer must contain only the reformulated question and nothing else.
                Never add follow-up messages, clarifications, notes, disclaimers, or requests for more information such as "if you give me more information, I can be more precise".
                """
        },
        ChatHistoryProvider = reformulationChatHistoryProvider
    });

var chatHistoryProvider = new InMemoryChatHistoryProvider(new()
{
    ChatReducer = new MessageCountingChatReducer(20),
    ReducerTriggerEvent = InMemoryChatHistoryProviderOptions.ChatReducerTriggerEvent.AfterMessageAdded,
    StorageInputRequestMessageFilter = messages =>
    {
        return messages.Where(m => m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.ChatHistory
            && m.GetAgentRequestMessageSourceType() != AgentRequestMessageSourceType.AIContextProvider);
    }
});

var ragAgent = openAIClient
    .AsAIAgent(new ChatClientAgentOptions
    {
        Name = "rag-agent",
        ChatOptions = new()
        {
            Instructions = """
                You are a helpful assistant. Answer questions using the provided context and cite the source document when available.
                You can use only the information provided in this chat to answer questions. If you don't know the answer, reply suggesting to refine the question.

                For example, if the user asks "What is the capital of Italy?" and in this chat there isn't information about Italy, you should reply something like:
                - This information isn't available in the given context.
                - I'm sorry, I don't know the answer to that question.
                - I don't have that information.
                - I don't know.
                - Given the context, I can't answer that question.
                - I'm sorry, I don't have enough information to answer that question.

                Never answer questions that are not related to this chat.
                """
        },
        ChatHistoryProvider = chatHistoryProvider,
        AIContextProviders = [new TextSearchProvider(new SearchProvider().SearchAsync, textSearchOptions)]
    });

//var ragAgent = AgentWorkflowBuilder.BuildSequential(reformulationAgent, agent).AsAIAgent(name: "rag-agent");

var session = await ragAgent.CreateSessionAsync();

while (true)
{
    Console.Write("Question: ");
    var question = Console.ReadLine()!;

    //var response = await ragAgent.RunAsync(question, session);

    var reformulationResponse = await reformulationAgent.RunAsync(question, session);

    var response = ragAgent.RunStreamingAsync(reformulationResponse.Text, session);
    await foreach (var update in response)
    {
        Console.Write(update);
    }

    session.TryGetInMemoryChatHistory(out var messages);

    //var response = await agent.RunAsync<Response>(question, session);

    //Console.WriteLine(response.Text);
    //if (response.Result.Citations != null && response.Result.Citations.Any())
    //{
    //    Console.WriteLine("Citations:");
    //    foreach (var citation in response.Result.Citations)
    //    {
    //        Console.WriteLine($"- {citation.Name} ({citation.Url}): {citation.Excerpt}");
    //    }
    //}

    Console.WriteLine();
    Console.WriteLine();
}

public class SearchProvider
{
    public Task<IEnumerable<TextSearchProvider.TextSearchResult>> SearchAsync(string query, CancellationToken cancellationToken)
    {
        List<TextSearchProvider.TextSearchResult> results = [];

        if (query.Contains("taggia", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new()
            {
                SourceName = "Taggia",
                SourceLink = "https://it.wikipedia.org/wiki/Taggia",
                Text = "Taggia (Tàggia in ligure) è un comune italiano di 13.958 abitanti della provincia di Imperia in Liguria. Per popolazione è il quarto comune della provincia, dopo Sanremo, Imperia e Ventimiglia. Il centro storico di Taggia è situato nell'immediato entroterra della valle Argentina, mentre l'abitato di Arma è una località balneare. Tra i due centri vi è la zona denominata Levà (il toponimo deriva dalla denominazione romana per indicare un'area rialzata).\r\n\r\nIl territorio comunale è tuttavia molto esteso, perché coincide con la bassa valle del torrente Argentina, dalla confluenza del torrente Oxentina, presso la località San Giorgio, fino al mare. Si tratta di un ampio settore di entroterra caratterizzato da estese colture - soprattutto oliveti - nella fascia collinare e da estesi boschi nella sua porzione montana, che raggiunge il monte Faudo, massima elevazione del comune con i suoi 1149 metri.\r\n\r\nAltre vette del territorio il monte Follia (1031 m), il monte Neveia (835 m), il monte Santa Maria (462 m), il monte Giamanassa (405 m)."
            });

            results.Add(new()
            {
                SourceName = "Storia di Taggia",
                SourceLink = "https://it.wikipedia.org/wiki/Taggia",
                Text = "Secondo fonti locali, i primitivi insediamenti umani andrebbero ricercati già nell'epoca preromana, dove gli storici non escludono un probabile luogo di culto - dedicato al dio ligure Belleno - nella zona denominata di Capo Don (nel comune di Riva Ligure). La più antica testimonianza del luogo risale tra il X e il VII secolo a.C., grazie al ritrovamento di antiche tombe cinerarie sul sovrastante monte Grange, dove sorgeva un castelliere ligure che aveva anche funzione di emporio commerciale, aperto alle importazioni da tutto il mar Mediterraneo. Subì quindi la dominazione dell'Impero romano a partire dal I secolo a.C. Ai piedi del capo continuò a funzionare un porto-canale, nei cui pressi, in età imperiale, vennero costruite alcune ville rustiche e una stazione di posta che nella celebre Tabula Peutingeriana era ricordata come Costa Balenae. La zona era servita dalla via Julia Augusta, che attraversava tutta la Liguria di Ponente. Presso Costa Balenae piegava verso l'interno, superando il torrente nei pressi dell'abitato attuale. Scavi archeologici intrapresi poco prima del 1940, e poi ampliati a partire dagli anni ottanta del XX secolo, hanno portato alla luce i resti di un edificio di culto con un'area sepolcrale e la vasca ottagonale di un importante battistero paleocristiano. All'interno della valle, tra il V e VI secolo, si sviluppò invece l'insediamento fortificato di Campomarzio o Castel San Giorgio, caposaldo del sistema difensivo bizantino in Liguria (il cosiddetto Limes).\r\n\r\nIl villaggio venne distrutto e abbandonato molto verosimilmente durante l'invasione longobarda di Rotari del 641, che portò alla decadenza anche di San Giorgio.\r\n\r\n\r\nL'antica abbazia di Nostra Signora del Canneto\r\nDa allora gli abitanti della zona cominciarono a popolare un nuovo insediamento, su una bassa colina a circa tre chilometri dalla costa, che a partire dal tardo X secolo è noto come \"Tabia\". Si ebbe fin dal VII secolo, la fondazione dell'abbazia di Nostra Signora del Canneto da parte dei monaci di San Colombano, che poi accolsero verso il IX secolo come per Bobbio, Pedona e Lerino la riforma della regola di San Benedetto.\r\n\r\n\r\nLe Alpi Marittime nel 1805, con Taggia nel suo cantone; Taggia era il comune più a est della provincia.\r\nTutta la zona fu oggetto di scorrerie saracene tra i secoli IX e X, ma è del tutto leggendaria la tradizione che vuole Taggia salvata da questi predoni grazie all'intervento miracoloso di Benedetto Revelli, vescovo di Albenga (alla cui diocesi Taggia appartenne fino al 1831) ritenuto originario proprio di Taggia e poi proclamato santo.\r\n\r\nLe incursioni saracene che spopolarono le coste, colpirono anche l'abbazia di Taggia. Nell'891, i Saraceni profanarono il monastero, lo demolirono, uccisero tutti i monaci e incendiarono la preziosa biblioteca. Essa fu nuovamente ricostruita, sempre dai monaci benedettini dell'abbazia di Santo Stefano di Genova, di proprietà bobbiese, sul finire del X secolo che si insediarono nel territorio di Taggia e Villaregia (l'odierna Santo Stefano al Mare).\r\n\r\nTaggia divenne, almeno dal 1153, dominio feudale dei Clavesana che nel 1228 cedettero il borgo alla Repubblica di Genova. Dal 1273 fu sede della podesteria locale, mantenendo una certa autonomia ed estendendo i propri poteri sulle vicine Arma, Ripa Tabie (l'odierna Riva Ligure) e parte del territorio di Pompeiana e Bussana (quest'ultima ora località di Sanremo). Nel 1381 adottò (o meglio rinnovò) i propri Statuti, che tra l'altro affidavano al podestà poteri giurisdizionali.\r\n\r\nNel XVII secolo fu attivo il poeta Stefano Rossi, che cantò le virtù delle genti e del borgo, fornendo una interessante testimonianza della vita all'epoca a Taggia.\r\n\r\nTaggia e la sua podesteria divenne fedele alleata della repubblica genovese, seguendone pertanto le sorti storiche fino alla soppressione della medesima nel 1797, e anche durante la successiva Repubblica Ligure. Il territorio taggiasco fu inquadrato nell'omonimo cantone, nella giurisdizione delle Palme, con capoluogo Sanremo. Dal 1805, con il passaggio della Repubblica Ligure nel Primo Impero francese, rientrò nella giurisdizione degli Ulivi e dal 1805 parte integrante del Dipartimento delle Alpi Marittime francese.\r\n\r\nFu annesso al Regno di Sardegna nel 1815 dopo il congresso di Vienna del 1814, a seguito della caduta di Napoleone Bonaparte. Facente parte del Regno d'Italia dal 1861, dal 1859 al 1926 il comune di Taggia fu compreso nel VI mandamento omonimo del circondario di Sanremo facente parte della provincia di Nizza (poi provincia di Porto Maurizio e, dal 1923, di Imperia)."
            });

            results.Add(new()
            {
                SourceName = "Economia di Taggia",
                SourceLink = "https://it.wikipedia.org/wiki/Taggia",
                Text = "Il comune basa la sua principale risorsa economica soprattutto sul turismo, maggiormente concentrato ad Arma. L'attività agricola, fiorente come negli altri comuni della riviera ponentina, si è sviluppato nella floricoltura in particolare nel settore delle fronde ornamentali e ranuncoli. Di sola sussistenza la coltivazione di prodotti agricoli come ortaggi e agrumi.\r\n\r\nMolto redditizia invece è la produzione di olio di oliva, grazie alle ampie coltivazioni di ulivi della varietà taggiasca, denominazione derivante proprio dalla località. In espansione anche l'attività industriale."
            });

            results.Add(new()
            {
                SourceName = "Eventi e sport a Taggia",
                SourceLink = "https://it.wikipedia.org/wiki/Taggia",
                Text = "Oltre alle festività patronali dell'11 marzo in onore della Madonna miracolosa, un evento di grande richiamo turistico e devozionale avviene a Taggia il secondo sabato di febbraio con la festività di san Benedetto Revelli (\"i Furgari\"). Si svolgono grandi falò, giochi pirotecnici, musica e vino tra le piazze e i vicoli del centro storico. La prima edizione della festa avvenne il 12 febbraio 1626: i tabiesi sciolsero un voto a san Benedetto Revelli, cui si erano affidati perché la città venisse risparmiata durante la guerra dei trent'anni. Nell'ultimo fine settimana di febbraio avviene una rievocazione storica (con corteo e drammaturgie) che racconta l'origine della festa e momenti di vita seicentesca. Il territorio comunale di Taggia è attraversato dalla pista ciclabile della Riviera Ligure, lunga 24 km, che da ovest verso est collega i vari comuni costieri di Ospedaletti, Sanremo, Arma di Taggia, Riva Ligure, Santo Stefano al Mare, Cipressa, Costarainera e San Lorenzo al Mare lungo il vecchio tracciato della ferrovia Genova-Ventimiglia."
            });
        }

        if (query.Contains("luna", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new()
            {
                SourceName = "Luna",
                SourceLink = "https://it.wikipedia.org/wiki/Luna",
                Text = "La Luna è l'unico satellite naturale della Terra. Il suo nome proprio viene talvolta utilizzato, per antonomasia e con l'iniziale minuscola («una luna»), come sinonimo di satellite anche per i corpi celesti che orbitano attorno ad altri pianeti.\r\n\r\nOrbita a una distanza media di circa 384400 km dalla Terra, sufficientemente vicina da essere osservabile a occhio nudo, così che sulla sua superficie è possibile distinguere delle macchie scure e delle macchie chiare. Le prime, dette mari, sono regioni quasi piatte coperte da rocce basaltiche e detriti di colore scuro. Le regioni lunari chiare, chiamate terre alte o altopiani, sono elevate di vari chilometri rispetto ai mari e presentano rilievi alti anche 8000-9000 metri. Essendo in rotazione sincrona rivolge sempre la stessa faccia verso la Terra e il suo lato nascosto è rimasto sconosciuto fino al periodo delle esplorazioni spaziali.\r\n\r\nDurante il suo moto orbitale, il diverso aspetto causato dall'orientazione rispetto al Sole genera delle fasi chiaramente visibili e che hanno influenzato il comportamento dell'uomo fin dall'antichità. Impersonata dai greci nella dea Selene, fu da tempo remoto considerata influente sui raccolti, le carestie e la fertilità. Condiziona la vita sulla Terra di molte specie viventi, regolandone il ciclo riproduttivo e i periodi di caccia; agisce sulle maree e sulla stabilità dell'asse di rotazione terrestre.\r\n\r\nSi pensa che la Luna si sia formata 4,5 miliardi di anni fa, non molto tempo dopo la nascita della Terra. Esistono diverse teorie riguardo alla sua formazione; la più accreditata è che si sia formata dall'aggregazione dei detriti rimasti in orbita dopo la collisione tra la Terra e un oggetto delle dimensioni di Marte chiamato Theia.\r\n\r\nIl suo simbolo astronomico ☾ è una rappresentazione stilizzata di una sua fase (compresa tra l'ultimo quarto e il novilunio visto dall'emisfero boreale, oppure tra il novilunio e il primo quarto visto dall'emisfero australe).\r\n\r\nLa faccia visibile della Luna è caratterizzata dalla presenza di circa 300.000 crateri da impatto (contando quelli con un diametro di almeno 1 km). Il cratere lunare più grande è il bacino Polo Sud-Aitken, che ha un diametro di circa 2500 km, è profondo 13 km e occupa la parte meridionale della faccia nascosta."
            });

            results.Add(new()
            {
                SourceName = "Formazione della Luna",
                SourceLink = "https://it.wikipedia.org/wiki/Formazione_della_Luna",
                Text = "Sono state proposte diverse ipotesi per spiegare la formazione della Luna che, in base alla datazione isotopica dei campioni di roccia lunare portati sulla Terra dagli astronauti delle missioni Apollo, risale a 4,527 ± 0,010 miliardi di anni fa, cioè circa 50 milioni di anni dopo la formazione del sistema solare.\r\n\r\nQueste ipotesi includono l'origine per fissione della crosta terrestre a causa della forza centrifuga, che però richiederebbe un valore iniziale troppo elevato per la rotazione terrestre; la cattura gravitazionale di un satellite già formatosi, che però richiederebbe un'enorme estensione dell'atmosfera terrestre per dissipare l'energia cinetica del satellite in transito; la co-formazione di entrambi i corpi celesti nel disco di accrescimento primordiale, che però non spiega la scarsità di ferro metallico sulla Luna. Nessuna di queste ipotesi inoltre è in grado di spiegare l'alto valore del momento angolare del sistema Terra-Luna.\r\n\r\nLa teoria più accreditata è quella secondo la quale essa si sia formata a seguito della collisione di un planetesimo, chiamato Theia, delle dimensioni simili a quelle di Marte con la Terra quando quest'ultima era ancora calda, nella prima fase della sua formazione. Il materiale scaturito dall'impatto sarebbe rimasto in orbita intorno alla Terra e per effetto della forza gravitazionale si sarebbe riaggregato formando la Luna. Detta comunemente la teoria dell'impatto gigante, è supportata da modelli teorici, pubblicati nell'agosto del 2001. Una conferma di questa tesi deriverebbe dal fatto che la composizione della Luna è pressoché identica a quella del mantello terrestre privato degli elementi più leggeri, evaporati per la mancanza di un'atmosfera e della forza gravitazionale necessarie per trattenerli. Inoltre, l'inclinazione dell'orbita della Luna rende piuttosto improbabili le teorie secondo cui essa si sarebbe formata insieme alla Terra o sarebbe stata catturata in seguito.\r\n\r\nUno studio del maggio del 2011 condotto dalla NASA porta elementi che appaiono in contraddizione con l'ipotesi dell'impatto gigante. Lo studio, eseguito su campioni vulcanici lunari, ha permesso di misurare nel magma lunare una concentrazione d'acqua 100 volte superiore a quella precedentemente stimata. Secondo la suddetta teoria, l'acqua avrebbe dovuto essere evaporata quasi completamente durante l'impatto; invece, i dati presentati nello studio conducono a stimare un quantitativo d'acqua simile a quello presente nella crosta terrestre.\r\n\r\nUn altro studio della NASA indica che la faccia nascosta potrebbe essere stata generata dalla fusione tra la Luna e una seconda luna della Terra, la quale si sarebbe distribuita uniformemente sulla faccia lontana della Luna che conosciamo. Questa teoria spiegherebbe anche perché il lato nascosto della luna si presenti più frastagliato e montuoso rispetto al lato visibile del satellite terrestre."
            });

            results.Add(new()
            {
                SourceName = "Osservazione della Luna",
                SourceLink = "https://it.wikipedia.org/wiki/Luna",
                Text = "Nell'antichità\r\n\r\nMappa della Luna di Johannes Hevelius dal suo Selenographia (1647), la prima mappa che include le zone di librazione\r\nNei tempi antichi non erano rare le culture, prevalentemente nomadi, che ritenevano che la Luna morisse ogni notte, scendendo nel mondo delle ombre; altre culture pensavano che la Luna inseguisse il Sole (o viceversa). Ai tempi di Pitagora, come enunciava la scuola pitagorica, era considerata un pianeta. Uno dei primi sviluppi dell'astronomia fu la comprensione dei cicli lunari. Già nel V secolo a.C. gli astronomi babilonesi registrarono i cicli di ripetizione (saros) delle eclissi lunari e gli astronomi indiani descrissero i moti di elongazione della Luna. Successivamente fu spiegata la forma apparente della Luna, le fasi, e la causa della Luna piena. Anassagora affermò per primo, nel 428 a.C., che Sole e Luna fossero delle rocce sferiche, con il primo a emettere luce che la seconda riflette. Sebbene i cinesi della dinastia Han credessero che la Luna avesse un'energia di tipo Ki, la loro teoria ammetteva che la luce della Luna fosse solo un riflesso di quella del Sole. Jing Fang, vissuto tra il 78 e il 37 a.C., notò anche che la Luna avesse una certa sfericità. Nel secondo secolo dopo Cristo, Luciano di Samosata scrisse un racconto dove gli eroi viaggiavano fino alla Luna scoprendo che era disabitata. Nel 499, l'astronomo indiano Aryabhata menzionò nella sua opera Aryabhatiya che la causa della brillantezza della Luna è proprio la riflessione della luce solare.\r\n\r\nDal medioevo al XX secolo\r\n\r\nXilografie sull'aspetto della Luna pubblicate nel Sidereus Nuncius (1610), ricavate dagli acquerelli di Galileo.\r\nAll'inizio del Medioevo alcuni credevano che la Luna fosse una sfera perfettamente liscia, come sosteneva la teoria aristotelica, e altri che vi si trovassero oceani (a tutt'oggi il termine «mare» è impiegato per designare le regioni più scure della superficie lunare). Il fisico Alhazen, a cavallo dell'anno 1000, scoprì che la luce solare non è riflessa dalla Luna come uno specchio, ma è riflessa dalla superficie in tutte le direzioni.\r\n\r\nQuando, nel 1609, Galileo puntò il suo telescopio sulla Luna, scoprì che la sua superficie non era liscia, bensì corrugata e composta da vallate, monti alti più di 8000 m e crateri. La stima dell'elevazione dei rilievi lunari fu oggetto di una brillante intuizione matematica: sfruttando la conoscenza del diametro lunare ed osservando la distanza delle vette montuose dal terminatore, l'astronomo toscano ne calcolò efficacemente l'altitudine; misurazioni moderne hanno confermato la presenza di monti che, avendo origine differente da quelli terrestri, data la minor gravità lunare, giungono ad 8 km di elevazione (il punto più alto misura 10750 m rispetto alla quota media).\r\n\r\nAncora agli inizi del Novecento c'erano dubbi sulla possibilità che la Luna potesse avere un'atmosfera respirabile. L'astronomo Alfonso Fresa, ponendosi il problema dell'abitabilità della Luna, la legava inscindibilmente alla presenza dell'acqua e dell'aria:\r\n\r\n«Innanzitutto bisogna intendersi sul significato della parola vita, la quale, se va intesa nel senso organico, molto difficilmente potrà ancora albergare sulla Luna, giacché mancano lassù i fattori necessari alla sua esistenza: l'aria e l'acqua. Si potrebbe obiettare che un'assenza completa di esse non debba essere presa alla lettera, perché pur non verificandosi nemmeno in piccolissima parte i fenomeni di rifrazione, un residuo sparutissimo di aria può esistere sul nostro satellite, per quanto anche l'analisi spettroscopica abbia confermato che il nostro satellite è completamente privo di atmosfera.»\r\n(Fresa, pp. 434-435)"
            });

            results.Add(new()
            {
                SourceName = "Dimensioni della Luna",
                Text = "La Luna ha dimensioni ben precise, studiate con grande accuratezza:\r\n\r\nDiametro medio: circa 3.474 km\r\nRaggio: circa 1.737 km\r\nCirconferenza: circa 10.921 km\r\nSuperficie: circa 38 milioni di km² (simile all’Africa!)\r\nVolume: circa 1/50 di quello della Terra\r\n\r\nPer confronto, la Luna è circa 1/4 del diametro della Terra, il che la rende uno dei satelliti più grandi in proporzione al pianeta che orbita."
            });
        }

        if (query.Contains("marte", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new()
            {
                SourceName = "Marte",
                SourceLink = "https://it.wikipedia.org/wiki/Marte_(astronomia)",
                Text = "Marte è il quarto pianeta del sistema solare in ordine di distanza dal Sole; è visibile a occhio nudo ed è uno dei pianeti di tipo terrestre (roccioso) come Mercurio, Venere e la Terra (1,52 unità astronomiche [227.000.000 km] di distanza dal Sole). Chiamato pianeta rosso per via del suo colore caratteristico causato dalla grande quantità di ossido di ferro che lo ricopre,Marte prende il nome dall'omonima divinità della mitologia romana e il suo simbolo astronomico è la rappresentazione stilizzata dello scudo e della lancia del dio (; Unicode: ♂).\r\n\r\nPur presentando temperature medie superficiali piuttosto basse (tra −120 e −14 °C) e un'atmosfera molto rarefatta, è il pianeta più simile alla Terra tra quelli del sistema solare. Le sue dimensioni sono intermedie tra quelle del nostro pianeta e quelle della Luna, e l'inclinazione del suo asse di rotazione e la durata del giorno sono molto simili a quelle terrestri. La sua superficie presenta formazioni vulcaniche, valli, calotte polari e deserti sabbiosi, e formazioni geologiche che vi suggeriscono la presenza di un'idrosfera in un lontano passato. La superficie del pianeta appare fortemente craterizzata, a causa della quasi totale assenza di agenti erosivi (principalmente, l'attività geologica, atmosferica e idrosferica) e dalla totale assenza di attività tettonica delle placche capace di formare e poi modellare le strutture tettoniche. La bassissima densità dell'atmosfera non è poi in grado di consumare buona parte delle meteore, che pertanto raggiungono il suolo con maggior frequenza che non sulla Terra. Tra le formazioni geologiche più notevoli di Marte si segnalano: l'Olympus Mons, o monte Olimpo, il vulcano più grande del sistema solare (alto 27 km); le Valles Marineris, un lungo canyon notevolmente più esteso di quelli terrestri; e un enorme cratere sull'emisfero boreale, ampio circa il 40% dell'intera superficie marziana.\r\n\r\nAll'osservazione diretta, Marte presenta variazioni di colore, imputate storicamente alla presenza di vegetazione stagionale, che si modificano al variare dei periodi dell'anno; ma successive osservazioni spettroscopiche dell'atmosfera hanno da tempo fatto abbandonare l'ipotesi che vi potessero essere mari, canali e fiumi oppure un'atmosfera sufficientemente densa. La smentita finale arrivò dalla missione Mariner 4, che nel 1965 mostrò un pianeta desertico e arido, animato da tempeste di sabbia periodiche e particolarmente violente. Le missioni più recenti hanno evidenziato la presenza di acqua ghiacciata.\r\n\r\nIntorno al pianeta orbitano due satelliti naturali, Fobos e Deimos, di piccole dimensioni e dalla forma irregolare."
            });

            results.Add(new()
            {
                SourceName = "Osservazione di Marte",
                SourceLink = "https://it.wikipedia.org/wiki/Osservazione_di_Marte",
                Text = "A occhio nudo Marte solitamente appare di un marcato colore giallo, arancione o rossastro e per luminosità è il più variabile nel corso della sua orbita tra tutti i pianeti esterni: la sua magnitudine apparente infatti passa da un minimo +1,8 fino a un massimo di −2,91 all'opposizione perielica (anche chiamata grande opposizione). A causa dell'eccentricità orbitale la sua distanza relativa varia a ogni opposizione determinando piccole e grandi opposizioni, con un diametro apparente da 13,5 a 25,1 secondi d'arco. Il 27 agosto 2003 alle 9:51:13 UT Marte si è trovato vicino alla Terra come mai in quasi 60000 anni: 55.758.006 km (0,37271925 au). Ciò è stato possibile perché Marte si trovava a un giorno dall'opposizione e circa a tre giorni dal suo perielio, cosa che lo rese particolarmente visibile dalla Terra. Tuttavia questo avvicinamento è solo di poco inferiore ad altri. Ad esempio il 22 agosto 1924 la distanza minima fu di 0,372846 unità astronomiche (55.777.000 km) e si prevede che il 24 agosto 2208 sarà di 0,37279 unità astronomiche (55.769.000 km). Il massimo avvicinamento di questo millennio avverrà invece l'8 settembre 2729, quando Marte si troverà a 0,372004 unità astronomiche (55.651.000 km) dalla Terra.\r\n\r\nCon l'osservazione al telescopio sono visibili alcuni dettagli caratteristici della superficie, che permisero agli astronomi dal sedicesimo al ventesimo secolo di speculare sull'esistenza di una civiltà organizzata sul pianeta. Basta un piccolo obiettivo da 70–80 mm per risolvere macchie chiare e scure sulla superficie e le calotte polari; già con un 100 millimetri si può riconoscere il Syrtis Major Planum. L'aiuto di filtri colorati permette inoltre di delineare meglio i bordi tra regioni di diversa natura geologica. Con un obiettivo da 250 mm e condizioni di visibilità ottimali sono visibili i caratteri principali della superficie, i rilievi e i canali. La visione di questi dettagli può essere parzialmente oscurata da tempeste di sabbia su Marte che possono estendersi fino a coprire tutto il pianeta.\r\n\r\n\r\nMoto retrogrado apparente di Marte nel 2003 visto dalla Terra (simulazione realizzata con Stellarium)\r\nL'avvicinarsi di Marte all'opposizione comporta l'inizio di un periodo di moto retrogrado apparente, durante il quale, se ci si riferisce alla volta celeste, il pianeta appare in moto nel verso opposto all'ordinario (quindi da est verso ovest anziché da ovest verso est) con la sua orbita che sembra formare un 'cappio' (in inglese \"loop\"); il moto retrogrado di Marte dura mediamente 72 giorni."
            });

            results.Add(new()
            {
                SourceName = "Parametri Orbitali di Marte",
                SourceLink = "https://it.wikipedia.org/wiki/Parametri_orbitali_di_Marte",
                Text = "Marte orbita attorno al Sole a una distanza media di circa 228 milioni di chilometri (1,52 au) e il suo periodo di rivoluzione è di circa 687 giorni (1 anno, 320 giorni e 18,2 ore terrestri). Il giorno solare di Marte (il Sol) è poco più lungo del nostro: 24 ore, 37 minuti e 23 secondi.\r\n\r\nL'inclinazione assiale marziana è di 25,19° che risulta simile a quella della Terra. Per questo motivo le stagioni si assomigliano eccezion fatta per la durata doppia su Marte. Inoltre il piano dell'orbita si discosta di circa 1,85° da quello dell'eclittica.\r\n\r\nA causa della discreta eccentricità della sua orbita, pari a 0,093, la sua distanza dalla Terra all'opposizione può oscillare fra circa 100 e circa 56 milioni di chilometri; solo Mercurio ha un'eccentricità superiore nel sistema solare. Tuttavia in passato Marte seguiva un'orbita molto più circolare: circa 1,35 milioni di anni fa la sua eccentricità era equivalente a 0,002, che è molto inferiore a quella terrestre attuale. Marte ha un ciclo di eccentricità di 96000 anni terrestri paragonati ai 100000 della Terra; negli ultimi 35000 anni l'orbita marziana è diventata sempre più eccentrica a causa delle influenze gravitazionali degli altri pianeti e il punto di maggior vicinanza tra Terra e Marte continuerà a diminuire nei prossimi 25000 anni."
            });
        }

        if (query.Contains("plutone", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new()
            {
                SourceName = "Plutone",
                SourceLink = "https://it.wikipedia.org/wiki/Plutone_(astronomia)",
                Text = "Plutone è un pianeta nano orbitante nella parte esterna del sistema solare, nella fascia di Kuiper. Scoperto da Clyde Tombaugh nel 1930, è stato considerato per 76 anni il nono pianeta del sistema solare.\r\n\r\nIl suo status di pianeta fu messo in discussione dal 1992 in seguito all'individuazione di diversi oggetti di dimensioni simili nella Fascia di Kuiper; la scoperta di Eris nel 2005, un pianeta nano del disco diffuso che è il 27% più massiccio di Plutone, ha portato infine l'Unione Astronomica Internazionale a riconsiderare – dopo un acceso dibattito – la definizione di pianeta e a riclassificare così Plutone come pianeta nano il 24 agosto 2006.\r\n\r\nFra i corpi celesti del sistema solare, Plutone è il sedicesimo per grandezza e il diciassettesimo per massa, ed è per diametro il più grande dei pianeti nani e degli oggetti transnettuniani conosciuti (in ambedue le categorie è superato come massa da Eris). Ha inoltre massa e dimensioni inferiori a quelle dei maggiori satelliti naturali del sistema solare: i satelliti medicei di Giove, Titano, Tritone e la Luna. Paragonato a quest'ultima in particolare, la sua massa è pari a un sesto e il volume a un terzo. Come gli altri oggetti della fascia di Kuiper, Plutone è composto principalmente di ghiaccio e roccia.\r\n\r\nLa sua orbita è piuttosto eccentrica e inclinata rispetto al piano dell'eclittica, mentre la sua distanza dal Sole varia da 30 fino a 49 au (4,5×109 fino a 7,3×109 km). Periodicamente Plutone, durante il suo perielio, viene a trovarsi più vicino al Sole di Nettuno, tuttavia essendo in risonanza orbitale 2:3 con esso, non gli si avvicina mai a meno di 17 au (2,5×109 km).\r\n\r\nPlutone ha cinque lune conosciute: Caronte (la più grande, con un diametro che è poco più della metà del suo), Stige, Notte, Cerbero e Idra. Plutone e Caronte vengono considerati un sistema binario o un pianeta doppio, poiché il baricentro del sistema giace al di fuori di entrambi.\r\n\r\nIl 14 luglio 2015, la sonda New Horizons è diventata la prima navicella spaziale a sorvolare Plutone, effettuando misure e osservazioni dettagliate del pianeta nano e delle sue lune. Nel settembre 2016, gli astronomi hanno annunciato che la calotta bruno-rossastra che ricopre il polo nord di Caronte è composta da toline, macromolecole organiche che possono essere ingredienti per la vita, e che, rilasciate dall'atmosfera di Plutone, precipitano su Caronte a 19.000 km di distanza."
            });

            results.Add(new()
            {
                SourceName = "Osservazione di Plutone",
                SourceLink = "https://it.wikipedia.org/wiki/Osservazione_di_Plutone",
                Text = "Osservato dalla Terra, Plutone presenta una magnitudine apparente media pari a 15,1 e raggiunge la sua massima luminosità nel periodo centrato sul perielio, arrivando a una magnitudine pari a 13,65. Il suo diametro angolare varia da un minimo di 0,06 a un massimo di 0,11 secondi d'arco, quando si trova alla minima distanza dal nostro pianeta. Queste caratteristiche ne rendono problematica l'osservazione e giustificano il fatto che sia stato scoperto solamente nella prima metà del XX secolo.\r\n\r\nPlutone non può essere osservato facilmente mediante piccoli strumenti amatoriali. Telescopi con apertura superiore a 200 mm dovrebbero permettere di scorgerlo, sebbene sia preferibile utilizzare aperture di almeno 300-350 mm per osservarlo.\r\n\r\nL'utilizzo sempre più diffuso di CCD in campo amatoriale permette, sotto un cielo con un buon seeing, di poter acquisire immagini anche del suo satellite Caronte, quando quest'ultimo si trova alla massima distanza angolare da Plutone."
            });
        }

        return Task.FromResult<IEnumerable<TextSearchProvider.TextSearchResult>>(results);
    }
}

public class TraceHttpClientHandler : HttpClientHandler
{
    private static readonly JsonSerializerOptions jsonSerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestString = await request.Content?.ReadAsStringAsync(cancellationToken)!;
        PrintText($"Raw Request ({request.RequestUri})", ConsoleColor.Green);

        PrintText(FormatJson(requestString), ConsoleColor.DarkGray);
        PrintSeparator();

        var response = await base.SendAsync(request, cancellationToken);

        return response;

        static void PrintText(string message, ConsoleColor color)
        {
            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(message);
            Console.ForegroundColor = originalColor;
        }

        static void PrintSeparator() => Console.WriteLine(new string('-', 50));
    }

    private static string FormatJson(string input)
    {
        try
        {
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(input);
            return JsonSerializer.Serialize(jsonElement, jsonSerializerOptions);
        }
        catch
        {
            return input;
        }
    }
}

public record class Response(string Text, IEnumerable<Citations> Citations);

public record class Citations(string Name, string Url, string Excerpt);